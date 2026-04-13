using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Approov;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ShapesApp : MonoBehaviour
{
    private const string ShapesHost = "https://shapes.approov.io/";
    private const string ApiKey = "yXClypapWNHIifHUWmBIyPFAm";
    private const string DiagnosticsFileName = "approov-shapes-diagnostics.log";
    private const string SampleLogTag = "ShapesApp";
    private const int HttpTimeoutSeconds = 20;
    private const int MaxVisibleLogLines = 10;

    private static readonly bool[] ApproovModes =
    {
        false,
        true
    };

    private static readonly ShapesTransportMode[] TransportModes =
    {
        ShapesTransportMode.UnityWebRequest,
        ShapesTransportMode.HttpClient
    };

    private static readonly ShapesEndpointVersion[] EndpointModes =
    {
        ShapesEndpointVersion.V1,
        ShapesEndpointVersion.V3,
        ShapesEndpointVersion.V5
    };

    private static readonly ShapesSignatureMode[] SignatureModes =
    {
        ShapesSignatureMode.None,
        ShapesSignatureMode.Install,
        ShapesSignatureMode.Account
    };

    public enum ShapesTransportMode
    {
        UnityWebRequest,
        HttpClient
    }

    public enum ShapesEndpointVersion
    {
        V1,
        V3,
        V5
    }

    public enum ShapesSignatureMode
    {
        None,
        Install,
        Account
    }

    private enum RequestKind
    {
        Hello,
        Shapes
    }

    [Serializable]
    private sealed class ShapesApiResponse
    {
        public string text;
        public string shape;
        public string status;
    }

    private sealed class ShapesEndpointMetadata
    {
        public string BaseUrl;
        public string Description;
        public bool RequiresApiKey;
        public bool RequiresApproov;
        public bool SupportsMessageSigning;
        public string Title;
    }

    private sealed class SampleRequestResult
    {
        public string Diagnostics;
        public string Error;
        public bool IsSuccess;
        public string ResponseBody;
        public long StatusCode;
    }

    private sealed class AutoTestScenario
    {
        public bool ApproovEnabled;
        public ShapesEndpointVersion Endpoint;
        public string Label;
        public RequestKind RequestKind;
        public ShapesSignatureMode SignatureMode;
        public ShapesTransportMode Transport;
    }

    private sealed class ShapesMessageSigningMutator : ApproovServiceMutator
    {
        private readonly ShapesApp _owner;
        private readonly ApproovDefaultMessageSigning _accountSigner;
        private readonly ApproovDefaultMessageSigning _installSigner;

        public ShapesMessageSigningMutator(ShapesApp owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

            _installSigner = new ApproovDefaultMessageSigning()
                .SetDefaultFactory(CreateFactory(ShapesSignatureMode.Install));
            _accountSigner = new ApproovDefaultMessageSigning()
                .SetDefaultFactory(CreateFactory(ShapesSignatureMode.Account));
        }

        public override string ToString()
        {
            return "ShapesMessageSigningMutator";
        }

        public override bool ShouldProcessRequest(ApproovRequestContext request)
        {
            return ApproovServiceMutator.Default.ShouldProcessRequest(request);
        }

        public override bool HandleInterceptorFetchTokenResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult)
        {
            try
            {
                bool shouldApply = ApproovServiceMutator.Default.HandleInterceptorFetchTokenResult(request, approovResult);
                _owner?.HandleApproovFetchResult(request, approovResult, shouldApply, null);
                return shouldApply;
            }
            catch (Exception exception)
            {
                _owner?.HandleApproovFetchResult(request, approovResult, false, exception.Message);
                throw;
            }
        }

        public override bool HandleHeaderSubstitutionResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult, string header)
        {
            return ApproovServiceMutator.Default.HandleHeaderSubstitutionResult(request, approovResult, header);
        }

        public override bool HandleQueryParamSubstitutionResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult, string queryKey)
        {
            return ApproovServiceMutator.Default.HandleQueryParamSubstitutionResult(request, approovResult, queryKey);
        }

        public override bool ShouldProcessPinning(ApproovRequestContext request)
        {
            return ApproovServiceMutator.Default.ShouldProcessPinning(request);
        }

        public override void HandleProcessedRequest(ApproovRequestContext request, ApproovRequestMutations changes)
        {
            if (_owner == null || request?.Uri == null || changes == null)
            {
                return;
            }

            if (!_owner.ShouldSignRequest(request))
            {
                return;
            }

            switch (_owner.GetSelectedSignatureMode())
            {
                case ShapesSignatureMode.Install:
                    _installSigner.HandleProcessedRequest(request, changes);
                    break;
                case ShapesSignatureMode.Account:
                    _accountSigner.HandleProcessedRequest(request, changes);
                    break;
            }
        }

        private static ApproovDefaultMessageSigning.SignatureParametersFactory CreateFactory(ShapesSignatureMode mode)
        {
            ApproovDefaultMessageSigning.SignatureParametersFactory factory =
                ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory()
                    .AddOptionalHeaders("Api-Key");

            if (mode == ShapesSignatureMode.Account)
            {
                factory.SetUseAccountMessageSigning();
            }
            else
            {
                factory.SetUseInstallMessageSigning();
            }

            return factory;
        }
    }

    public Button helloButton;
    public Button shapesButton;
    public Text statusText;
    public Image shapesImage;

    [SerializeField] private bool defaultApproovEnabled = true;
    [SerializeField] private ShapesTransportMode defaultTransport = ShapesTransportMode.UnityWebRequest;
    [SerializeField] private ShapesEndpointVersion defaultEndpoint = ShapesEndpointVersion.V3;
    [SerializeField] private ShapesSignatureMode defaultSignatureMode = ShapesSignatureMode.Install;
    [SerializeField] private bool enableDetailedServiceLogging = true;
    [SerializeField] private bool useApproovDevKey = false;
    [SerializeField] private string approovDevKey = string.Empty;

    private readonly Dictionary<string, Sprite> _imageSprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture2D> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _persistentLogLock = new();
    private readonly List<string> _screenLogLines = new();

    private int _activeRequestId;
    private Button _autoTestButton;
    private Text _currentConfigText;
    private Text _endpointDescriptionText;
    private Text _logText;
    private bool _isAutoTestRunning;
    private bool _isRequestInFlight;
    private string _logFilePath;
    private ShapesMessageSigningMutator _messageSigningMutator;
    private string _approovInitializationError;
    private Dropdown _endpointDropdown;
    private int _requestSequence;
    private Toggle _approovToggle;
    private Dropdown _signatureDropdown;
    private Dropdown _transportDropdown;

    private void Start()
    {
        if (!LoadImageResources())
        {
            throw new Exception("Failed to load images");
        }

        if (helloButton == null || shapesButton == null || statusText == null || shapesImage == null)
        {
            throw new InvalidOperationException("Shapes sample scene is missing one or more required UI references.");
        }

        InitializePersistentLogging();
        ApproovService.SetDetailedDebugLogging(enableDetailedServiceLogging);
        LogConfiguredDevKeyState();
        helloButton.onClick.AddListener(OnHelloButtonClicked);
        shapesButton.onClick.AddListener(OnShapesButtonClicked);

        ConfigureBaseLayout();
        CreateRuntimeControls();
        ApplyDefaultSelections();

        SelectImage("approov");
        statusText.text = "Choose a transport and endpoint, then press Hello, Get Shape, or Run Auto Test.";

        ClearScreenLog();
        LogSample("Sample started on " + Application.platform + ". Detailed service logging is " +
                  (enableDetailedServiceLogging ? "enabled." : "disabled."));
        LogSample("Persistent diagnostics log: " + _logFilePath);

        RefreshConfigurationUi();
        ApplyMutatorConfiguration();
    }

    private void LogConfiguredDevKeyState()
    {
        if (!IsDevKeyConfigured())
        {
            LogSample("Approov dev key disabled for this sample build.");
            return;
        }

        LogSampleWarning("Approov dev key configured for this sample build and will be applied after SDK initialization. " +
                         "Do not ship a production app with a dev key because it bypasses normal attestation checks.");
    }

    private void ApplyConfiguredDevKey()
    {
        if (!IsDevKeyConfigured())
        {
            return;
        }

        ApproovService.SetDevKey(approovDevKey);
        LogSampleWarning("Approov dev key configured for this sample build. sdkInitialized=" + ApproovService.IsSDKInitialized() +
                         ". Do not ship a production app with a dev key because it causes attestation to pass.");
    }

    private bool IsDevKeyConfigured()
    {
        return useApproovDevKey && !string.IsNullOrWhiteSpace(approovDevKey);
    }

    private void InitializePersistentLogging()
    {
        _logFilePath = Path.Combine(Application.persistentDataPath, DiagnosticsFileName);
        Application.logMessageReceivedThreaded -= HandleUnityLogMessage;
        Application.logMessageReceivedThreaded += HandleUnityLogMessage;

        try
        {
            string sessionHeader =
                "===== Shapes diagnostics session " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") +
                " | platform=" + Application.platform +
                " | unity=" + Application.unityVersion +
                " | persistentDataPath=" + Application.persistentDataPath + " =====";
            AppendPersistentLogLine(sessionHeader);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[" + SampleLogTag + "] Failed to initialize persistent logging: " + exception.Message);
        }
    }

    private void HandleUnityLogMessage(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        string header = "[" + DateTime.UtcNow.ToString("HH:mm:ss.fff") + "] [" + type + "] " + condition;
        AppendPersistentLogLine(header);
        if (!string.IsNullOrWhiteSpace(stackTrace))
        {
            AppendPersistentLogLine(stackTrace);
        }
    }

    private void AppendPersistentLogLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        try
        {
            lock (_persistentLogLock)
            {
                File.AppendAllText(_logFilePath, message + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= HandleUnityLogMessage;
        ApproovService.SetServiceMutator(ApproovServiceMutator.Default);
    }

    public void OnHelloButtonClicked()
    {
        StartRequest(RequestKind.Hello);
    }

    public void OnShapesButtonClicked()
    {
        StartRequest(RequestKind.Shapes);
    }

    private void OnAutoTestButtonClicked()
    {
        if (_isRequestInFlight || _isAutoTestRunning)
        {
            return;
        }

        StartCoroutine(RunAutoTestCoroutine());
    }

    private void StartRequest(RequestKind requestKind)
    {
        if (_isRequestInFlight || _isAutoTestRunning)
        {
            return;
        }

        StartCoroutine(RunInteractiveRequestCoroutine(requestKind));
    }

    private IEnumerator RunInteractiveRequestCoroutine(RequestKind requestKind)
    {
        int requestId = NextRequestId();
        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());
        if (!PrepareRequestForExecution(out string initError))
        {
            string message = "Approov initialization failed: " + initError;
            statusText.text = message;
            SelectImage("confused");
            LogSampleWarning(message);
            yield break;
        }

        BeginRequest(requestId, requestKind, null);
        LogOutgoingRequest(requestId, "Manual request", requestKind, metadata);

        SampleRequestResult result = null;
        yield return StartCoroutine(ExecuteCurrentRequestCoroutine(requestKind, value => result = value));

        ApplyRequestResult(requestKind, result);
        LogRequestResult(requestId, "Manual request complete", requestKind, result);
        EndRequest();
    }

    private IEnumerator RunAutoTestCoroutine()
    {
        bool previousApproovEnabled = IsApproovEnabled();
        ShapesTransportMode previousTransport = GetSelectedTransport();
        ShapesEndpointVersion previousEndpoint = GetSelectedEndpoint();
        ShapesSignatureMode previousSignature = GetSelectedSignatureMode();

        _isAutoTestRunning = true;
        UpdateInteractiveState();
        ClearScreenLog();
        SelectImage("approov");
        statusText.text = "Running automated Shapes scenarios...";

        List<AutoTestScenario> scenarios = BuildAutoTestScenarios();
        bool approovRuntimeSupported = IsApproovRuntimeSupported();
        bool approovInitializationAttempted = false;
        bool approovInitializationSucceeded = ApproovService.IsSDKInitialized();
        string approovInitializationError = _approovInitializationError;

        int passed = 0;
        int failed = 0;
        int skipped = 0;

        LogSample("Auto test started with " + scenarios.Count + " scenarios.");

        for (int index = 0; index < scenarios.Count; index++)
        {
            int requestId = NextRequestId();
            AutoTestScenario scenario = scenarios[index];
            SetSelectionsWithoutNotify(
                scenario.ApproovEnabled,
                scenario.Transport,
                scenario.Endpoint,
                scenario.SignatureMode);
            RefreshConfigurationUi();

            if (scenario.ApproovEnabled && !ApproovService.IsSDKInitialized() && !approovInitializationAttempted)
            {
                approovInitializationAttempted = true;
                approovInitializationSucceeded = EnsureApproovInitialized();
                approovInitializationError = _approovInitializationError;
            }

            if (scenario.ApproovEnabled && !approovInitializationSucceeded)
            {
                bool shouldSkip = !approovRuntimeSupported;
                string reason = shouldSkip
                    ? "Approov scenarios are skipped because native initialization is only available on Android and iOS player builds."
                    : "Approov initialization failed: " + approovInitializationError;
                string summary = BuildAutoTestScenarioSummary(
                    scenario,
                    shouldSkip ? "SKIP" : "FAIL",
                    reason);

                if (shouldSkip)
                {
                    skipped++;
                    LogSampleWarning(summary);
                }
                else
                {
                    failed++;
                    LogSampleError(summary);
                }

                continue;
            }

            ApplyMutatorConfiguration();

            BeginRequest(requestId, scenario.RequestKind, "[" + (index + 1) + "/" + scenarios.Count + "]");
            LogOutgoingRequest(requestId, "Scenario " + (index + 1) + "/" + scenarios.Count + " " + scenario.Label, scenario.RequestKind, GetEndpointMetadata(scenario.Endpoint));

            SampleRequestResult result = null;
            yield return StartCoroutine(ExecuteCurrentRequestCoroutine(scenario.RequestKind, value => result = value));

            ApplyRequestResult(scenario.RequestKind, result);
            EndRequest();

            bool scenarioPassed = EvaluateScenario(scenario, result, out string expectationSummary);
            string autoTestSummary = BuildAutoTestScenarioSummary(
                scenario,
                scenarioPassed ? "PASS" : "FAIL",
                expectationSummary);

            if (scenarioPassed)
            {
                passed++;
                LogSample(autoTestSummary);
            }
            else
            {
                failed++;
                LogSampleError(autoTestSummary);
            }

            yield return null;
        }

        SetSelectionsWithoutNotify(previousApproovEnabled, previousTransport, previousEndpoint, previousSignature);
        ApplyMutatorConfiguration();
        RefreshConfigurationUi();

        _isAutoTestRunning = false;
        UpdateInteractiveState();

        string finalSummary = "Auto test complete: " + passed + " passed, " + failed + " failed, " + skipped + " skipped.";
        statusText.text = finalSummary;
        LogSample(finalSummary);
    }

    private bool PrepareRequestForExecution(out string initError)
    {
        initError = null;
        if (IsApproovEnabled() && !EnsureApproovInitialized())
        {
            initError = string.IsNullOrWhiteSpace(_approovInitializationError)
                ? "Approov initialization did not complete successfully."
                : _approovInitializationError;
            return false;
        }

        ApplyMutatorConfiguration();
        return true;
    }

    private void BeginRequest(int requestId, RequestKind requestKind, string contextLabel)
    {
        _activeRequestId = requestId;
        _isRequestInFlight = true;
        UpdateInteractiveState();

        string prefix = string.IsNullOrWhiteSpace(contextLabel) ? string.Empty : contextLabel + " ";
        statusText.text = prefix + "[req-" + requestId + "] Sending " + GetRequestKindLabel(requestKind) + " via " + GetTransportLabel(GetSelectedTransport()) + "...";
    }

    private void EndRequest()
    {
        _activeRequestId = 0;
        _isRequestInFlight = false;
        UpdateInteractiveState();
        RefreshConfigurationUi();
    }

    private IEnumerator ExecuteCurrentRequestCoroutine(RequestKind requestKind, Action<SampleRequestResult> onComplete)
    {
        SampleRequestResult result = null;
        if (GetSelectedTransport() == ShapesTransportMode.HttpClient)
        {
            Task<SampleRequestResult> task = SendHttpClientRequestAsync(requestKind);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            result = task.Status == TaskStatus.RanToCompletion
                ? task.Result
                : new SampleRequestResult
                {
                    IsSuccess = false,
                    Error = task.Exception == null ? "Unknown HttpClient task failure" : FormatExceptionMessage(task.Exception),
                    Diagnostics = task.Exception?.ToString(),
                };
        }
        else
        {
            yield return StartCoroutine(SendUnityWebRequestCoroutine(requestKind, value => result = value));
        }

        onComplete?.Invoke(result ?? new SampleRequestResult
        {
            IsSuccess = false,
            Error = "No request result was produced.",
        });
    }

    private IEnumerator SendUnityWebRequestCoroutine(RequestKind requestKind, Action<SampleRequestResult> onComplete)
    {
        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());
        UnityWebRequest request = CreateUnityWebRequest(requestKind, metadata);
        UnityWebRequestAsyncOperation operation;

        try
        {
            operation = IsApproovEnabled()
                ? ApproovService.SendWebRequest(request)
                : request.SendWebRequest();
        }
        catch (Exception exception)
        {
            request.Dispose();
            onComplete?.Invoke(new SampleRequestResult
            {
                IsSuccess = false,
                Error = FormatExceptionMessage(exception),
                Diagnostics = exception.ToString(),
            });
            yield break;
        }

        yield return operation;

        SampleRequestResult result = BuildUnityWebRequestResult(request);
        request.Dispose();
        onComplete?.Invoke(result);
    }

    private async Task<SampleRequestResult> SendHttpClientRequestAsync(RequestKind requestKind)
    {
        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());

        try
        {
            using HttpClient client = CreateSampleHttpClient();
            using HttpRequestMessage request = CreateHttpRequestMessage(requestKind, metadata);
            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string body = response.Content == null ? null : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return new SampleRequestResult
            {
                IsSuccess = response.IsSuccessStatusCode,
                StatusCode = (long)response.StatusCode,
                ResponseBody = body,
                Error = response.IsSuccessStatusCode ? null : response.ReasonPhrase,
                Diagnostics = BuildHttpResponseDiagnostics(request, response, body),
            };
        }
        catch (Exception exception)
        {
            return new SampleRequestResult
            {
                IsSuccess = false,
                Error = FormatExceptionMessage(exception),
                ResponseBody = null,
                StatusCode = 0,
                Diagnostics = exception.ToString(),
            };
        }
    }

    private HttpClient CreateSampleHttpClient()
    {
        HttpClientHandler handler = new();
        HttpClient client = IsApproovEnabled()
            ? ApproovService.CreateHttpClient(handler)
            : new HttpClient(handler, disposeHandler: true);
        client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);
        return client;
    }

    private bool LoadImageResources()
    {
        Texture2D[] textures = Resources.LoadAll<Texture2D>("Images");
        if (textures == null || textures.Length == 0)
        {
            Debug.LogError("No images found in the Resources/Images folder");
            return false;
        }

        _images.Clear();
        _imageSprites.Clear();
        foreach (Texture2D texture in textures)
        {
            if (texture == null || _images.ContainsKey(texture.name))
            {
                continue;
            }

            _images.Add(texture.name, texture);
            _imageSprites.Add(
                texture.name,
                Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f)));
        }

        return _images.Count > 0;
    }

    private void ConfigureBaseLayout()
    {
        RectTransform imageRect = shapesImage.rectTransform;
        imageRect.anchorMin = new Vector2(0.5f, 1f);
        imageRect.anchorMax = new Vector2(0.5f, 1f);
        imageRect.pivot = new Vector2(0.5f, 1f);
        imageRect.anchoredPosition = new Vector2(0f, -470f);
        imageRect.sizeDelta = new Vector2(220f, 180f);

        RectTransform statusRect = statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 1f);
        statusRect.anchorMax = new Vector2(0.5f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.anchoredPosition = new Vector2(0f, -670f);
        statusRect.sizeDelta = new Vector2(360f, 96f);
        statusText.alignment = TextAnchor.UpperCenter;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform buttonsContainer = helloButton.transform.parent as RectTransform;
        if (buttonsContainer != null)
        {
            buttonsContainer.anchorMin = new Vector2(0.5f, 0f);
            buttonsContainer.anchorMax = new Vector2(0.5f, 0f);
            buttonsContainer.pivot = new Vector2(0.5f, 0f);
            buttonsContainer.anchoredPosition = new Vector2(0f, 32f);
            buttonsContainer.sizeDelta = new Vector2(320f, 72f);
        }
    }

    private void CreateRuntimeControls()
    {
        Canvas canvas = statusText.canvas;
        if (canvas == null)
        {
            throw new InvalidOperationException("Shapes sample could not locate the Canvas.");
        }

        DefaultControls.Resources resources = CreateDefaultControlResources();
        GameObject panelObject = DefaultControls.CreatePanel(resources);
        panelObject.name = "ShapesSettingsPanel";
        panelObject.transform.SetParent(canvas.transform, false);

        Image panelImage = panelObject.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = new Color(0.09f, 0.15f, 0.25f, 0.92f);
        }

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -16f);
        panelRect.sizeDelta = new Vector2(380f, 420f);

        VerticalLayoutGroup panelLayout = panelObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(14, 14, 14, 14);
        panelLayout.spacing = 8f;
        panelLayout.childAlignment = TextAnchor.UpperLeft;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        ContentSizeFitter panelFitter = panelObject.AddComponent<ContentSizeFitter>();
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _currentConfigText = CreatePanelText(panelObject.transform, 17, FontStyle.Bold, 30f);
        _endpointDescriptionText = CreatePanelText(panelObject.transform, 13, FontStyle.Normal, 70f);
        _endpointDescriptionText.alignment = TextAnchor.UpperLeft;
        _endpointDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _endpointDescriptionText.verticalOverflow = VerticalWrapMode.Overflow;

        _approovToggle = CreateToggle(panelObject.transform, "Approov Enabled", OnApproovToggleChanged, resources);
        _transportDropdown = CreateDropdownRow(
            panelObject.transform,
            "Transport",
            new[] { "UnityWebRequest", "HttpClient" },
            OnTransportDropdownChanged,
            resources);
        _endpointDropdown = CreateDropdownRow(
            panelObject.transform,
            "Endpoint",
            new[] { "v1", "v3", "v5" },
            OnEndpointDropdownChanged,
            resources);
        _signatureDropdown = CreateDropdownRow(
            panelObject.transform,
            "Signature",
            new[] { "None", "Install", "Account" },
            OnSignatureDropdownChanged,
            resources);
        _autoTestButton = CreatePanelButton(panelObject.transform, "Run Auto Test", OnAutoTestButtonClicked, resources);
        _logText = CreatePanelText(panelObject.transform, 12, FontStyle.Normal, 120f);
        _logText.alignment = TextAnchor.UpperLeft;
        _logText.color = new Color(0.87f, 0.92f, 1f, 1f);
        _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _logText.verticalOverflow = VerticalWrapMode.Overflow;
        _logText.text = "No activity yet.";
    }

    private DefaultControls.Resources CreateDefaultControlResources()
    {
        Sprite sprite = (helloButton.targetGraphic as Image)?.sprite;
        return new DefaultControls.Resources
        {
            standard = sprite,
            background = sprite,
            inputField = sprite,
            knob = sprite,
            checkmark = sprite,
            dropdown = sprite,
            mask = sprite,
        };
    }

    private void ApplyDefaultSelections()
    {
        SetSelectionsWithoutNotify(defaultApproovEnabled, defaultTransport, defaultEndpoint, defaultSignatureMode);
    }

    private void SetSelectionsWithoutNotify(
        bool approovEnabled,
        ShapesTransportMode transport,
        ShapesEndpointVersion endpoint,
        ShapesSignatureMode signature)
    {
        _approovToggle.SetIsOnWithoutNotify(approovEnabled);
        _transportDropdown.SetValueWithoutNotify(IndexOf(TransportModes, transport));
        _endpointDropdown.SetValueWithoutNotify(IndexOf(EndpointModes, endpoint));
        _signatureDropdown.SetValueWithoutNotify(IndexOf(SignatureModes, signature));
        _transportDropdown.RefreshShownValue();
        _endpointDropdown.RefreshShownValue();
        _signatureDropdown.RefreshShownValue();
    }

    private Text CreatePanelText(Transform parent, int fontSize, FontStyle fontStyle, float preferredHeight)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = statusText.font;
        text.color = Color.white;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        LayoutElement layoutElement = textObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = preferredHeight;

        return text;
    }

    private Toggle CreateToggle(Transform parent, string label, Action<bool> onChanged, DefaultControls.Resources resources)
    {
        GameObject toggleObject = DefaultControls.CreateToggle(resources);
        toggleObject.name = "ApproovToggle";
        toggleObject.transform.SetParent(parent, false);

        Toggle toggle = toggleObject.GetComponent<Toggle>();
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(value => onChanged(value));

        Text labelText = toggleObject.GetComponentInChildren<Text>(true);
        if (labelText != null)
        {
            labelText.font = statusText.font;
            labelText.fontSize = 14;
            labelText.color = Color.white;
            labelText.text = label;
        }

        StyleToggleVisuals(toggleObject, toggle);

        LayoutElement layoutElement = toggleObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 30f;
        return toggle;
    }

    private void StyleToggleVisuals(GameObject toggleObject, Toggle toggle)
    {
        RectTransform backgroundRect = toggleObject.transform.Find("Background") as RectTransform;
        if (backgroundRect == null)
        {
            return;
        }

        Image backgroundImage = backgroundRect?.GetComponent<Image>();
        if (backgroundRect != null)
        {
            backgroundRect.sizeDelta = new Vector2(22f, 22f);
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = new Color(0.95f, 0.97f, 1f, 1f);
        }

        Transform defaultCheckmark = backgroundRect?.Find("Checkmark");
        if (defaultCheckmark != null)
        {
            defaultCheckmark.gameObject.SetActive(false);
        }

        GameObject checkmarkObject = new GameObject("CheckmarkText", typeof(RectTransform));
        checkmarkObject.transform.SetParent(backgroundRect, false);

        Text checkmarkText = checkmarkObject.AddComponent<Text>();
        checkmarkText.font = statusText.font;
        checkmarkText.text = "✓";
        checkmarkText.fontSize = 17;
        checkmarkText.fontStyle = FontStyle.Bold;
        checkmarkText.alignment = TextAnchor.MiddleCenter;
        checkmarkText.color = new Color(0.12f, 0.62f, 0.28f, 1f);

        RectTransform checkmarkRect = checkmarkText.rectTransform;
        checkmarkRect.anchorMin = Vector2.zero;
        checkmarkRect.anchorMax = Vector2.one;
        checkmarkRect.offsetMin = Vector2.zero;
        checkmarkRect.offsetMax = Vector2.zero;

        toggle.targetGraphic = backgroundImage;
        toggle.graphic = checkmarkText;
    }

    private Dropdown CreateDropdownRow(
        Transform parent,
        string labelText,
        IReadOnlyList<string> options,
        Action<int> onChanged,
        DefaultControls.Resources resources)
    {
        GameObject rowObject = new GameObject(labelText + "Row", typeof(RectTransform));
        rowObject.transform.SetParent(parent, false);

        HorizontalLayoutGroup rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childForceExpandWidth = false;

        LayoutElement rowElement = rowObject.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 32f;

        Text label = CreatePanelText(rowObject.transform, 14, FontStyle.Normal, 24f);
        label.text = labelText;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleLeft;
        LayoutElement labelElement = label.GetComponent<LayoutElement>();
        labelElement.preferredWidth = 120f;
        labelElement.flexibleWidth = 0f;

        GameObject dropdownObject = DefaultControls.CreateDropdown(resources);
        dropdownObject.name = labelText + "Dropdown";
        dropdownObject.transform.SetParent(rowObject.transform, false);

        Dropdown dropdown = dropdownObject.GetComponent<Dropdown>();
        dropdown.options.Clear();
        for (int index = 0; index < options.Count; index++)
        {
            dropdown.options.Add(new Dropdown.OptionData(options[index]));
        }

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.onValueChanged.AddListener(value => onChanged(value));

        Image dropdownImage = dropdownObject.GetComponent<Image>();
        if (dropdownImage != null)
        {
            dropdownImage.color = new Color(0.95f, 0.97f, 1f, 1f);
        }

        LayoutElement dropdownLayout = dropdownObject.AddComponent<LayoutElement>();
        dropdownLayout.preferredWidth = 190f;
        dropdownLayout.flexibleWidth = 1f;

        foreach (Text text in dropdownObject.GetComponentsInChildren<Text>(true))
        {
            text.font = statusText.font;
            text.fontSize = 13;
            text.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        }

        StyleDropdownVisuals(dropdownObject, dropdown);
        return dropdown;
    }

    private void StyleDropdownVisuals(GameObject dropdownObject, Dropdown dropdown)
    {
        Transform arrowTransform = dropdownObject.transform.Find("Arrow");
        if (arrowTransform != null)
        {
            Image arrowImage = arrowTransform.GetComponent<Image>();
            if (arrowImage != null)
            {
                arrowImage.enabled = false;
            }

            GameObject arrowTextObject = new GameObject("ArrowText", typeof(RectTransform));
            arrowTextObject.transform.SetParent(arrowTransform, false);
            Text arrowText = arrowTextObject.AddComponent<Text>();
            arrowText.font = statusText.font;
            arrowText.text = "▼";
            arrowText.fontSize = 12;
            arrowText.fontStyle = FontStyle.Bold;
            arrowText.alignment = TextAnchor.MiddleCenter;
            arrowText.color = new Color(0.19f, 0.26f, 0.38f, 1f);

            RectTransform arrowTextRect = arrowText.rectTransform;
            arrowTextRect.anchorMin = Vector2.zero;
            arrowTextRect.anchorMax = Vector2.one;
            arrowTextRect.offsetMin = Vector2.zero;
            arrowTextRect.offsetMax = Vector2.zero;
        }

        Transform templateTransform = dropdownObject.transform.Find("Template");
        if (templateTransform == null)
        {
            return;
        }

        Image templateImage = templateTransform.GetComponent<Image>();
        if (templateImage != null)
        {
            templateImage.color = new Color(0.95f, 0.97f, 1f, 0.98f);
        }

        Transform scrollbarTransform = templateTransform.Find("Scrollbar");
        if (scrollbarTransform != null)
        {
            scrollbarTransform.gameObject.SetActive(false);
        }

        RectTransform viewport = templateTransform.Find("Viewport") as RectTransform;
        if (viewport != null)
        {
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
        }

        Transform itemTransform = templateTransform.Find("Viewport/Content/Item");
        if (itemTransform != null)
        {
            Image itemBackground = itemTransform.Find("Item Background")?.GetComponent<Image>();
            if (itemBackground != null)
            {
                itemBackground.color = new Color(1f, 1f, 1f, 0.96f);
            }

            Image itemCheckmark = itemTransform.Find("Item Checkmark")?.GetComponent<Image>();
            if (itemCheckmark != null)
            {
                itemCheckmark.color = new Color(0.16f, 0.58f, 0.31f, 1f);
            }

            Text itemLabel = itemTransform.Find("Item Label")?.GetComponent<Text>();
            if (itemLabel != null)
            {
                itemLabel.font = statusText.font;
                itemLabel.fontSize = 13;
                itemLabel.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            }
        }

        templateTransform.gameObject.SetActive(false);
    }

    private Button CreatePanelButton(Transform parent, string label, Action onClick, DefaultControls.Resources resources)
    {
        GameObject buttonObject = DefaultControls.CreateButton(resources);
        buttonObject.name = label.Replace(" ", string.Empty);
        buttonObject.transform.SetParent(parent, false);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick());

        Text buttonText = buttonObject.GetComponentInChildren<Text>(true);
        if (buttonText != null)
        {
            buttonText.font = statusText.font;
            buttonText.fontSize = 14;
            buttonText.fontStyle = FontStyle.Bold;
            buttonText.color = Color.white;
            buttonText.text = label;
        }

        Image buttonImage = buttonObject.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = new Color(0.18f, 0.44f, 0.78f, 1f);
        }

        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.18f, 0.44f, 0.78f, 1f);
        colors.highlightedColor = new Color(0.24f, 0.53f, 0.89f, 1f);
        colors.pressedColor = new Color(0.11f, 0.34f, 0.64f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.32f, 0.36f, 0.42f, 0.75f);
        button.colors = colors;

        LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 36f;
        return button;
    }

    private void OnApproovToggleChanged(bool value)
    {
        LogSample("Approov toggled " + (value ? "on." : "off."));
        ApplyMutatorConfiguration();
        RefreshConfigurationUi();
    }

    private void OnTransportDropdownChanged(int _)
    {
        LogSample("Transport changed to " + GetTransportLabel(GetSelectedTransport()) + ".");
        RefreshConfigurationUi();
    }

    private void OnEndpointDropdownChanged(int _)
    {
        LogSample("Endpoint changed to " + GetEndpointMetadata(GetSelectedEndpoint()).Title + ".");
        ApplyMutatorConfiguration();
        RefreshConfigurationUi();
    }

    private void OnSignatureDropdownChanged(int _)
    {
        LogSample("Signature mode changed to " + GetSelectedSignatureMode() + ".");
        ApplyMutatorConfiguration();
        RefreshConfigurationUi();
    }

    private void RefreshConfigurationUi()
    {
        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());
        _endpointDescriptionText.text = metadata.Description;
        _signatureDropdown.interactable = !_isRequestInFlight && !_isAutoTestRunning && metadata.SupportsMessageSigning;

        string approovState = IsApproovEnabled() ? "Approov On" : "Approov Off";
        if (IsApproovEnabled() && !ApproovService.IsSDKInitialized() && !string.IsNullOrWhiteSpace(_approovInitializationError))
        {
            approovState += " (needs init)";
        }

        _currentConfigText.text =
            GetTransportLabel(GetSelectedTransport()) + " | " +
            approovState + " | " +
            metadata.Title + " | " +
            GetSignatureSummary(metadata);
    }

    private void ApplyMutatorConfiguration()
    {
        if (!IsApproovEnabled() || !ApproovService.IsSDKInitialized())
        {
            ApproovService.SetServiceMutator(ApproovServiceMutator.Default);
            return;
        }

        _messageSigningMutator ??= new ShapesMessageSigningMutator(this);
        ApproovService.SetServiceMutator(_messageSigningMutator);
    }

    private bool EnsureApproovInitialized()
    {
        if (ApproovService.IsSDKInitialized())
        {
            ApplyConfiguredDevKey();
            _approovInitializationError = null;
            return true;
        }

        try
        {
            ApproovService.Initialize();
            ApplyConfiguredDevKey();
        }
        catch (Exception exception)
        {
            _approovInitializationError = exception.Message;
            LogSampleWarning("Approov initialization failed: " + exception.Message);
            return false;
        }

        if (!ApproovService.IsSDKInitialized())
        {
            _approovInitializationError =
                "Approov remains unavailable on this platform. Build for Android or iOS with a valid config string.";
            return false;
        }

        _approovInitializationError = null;
        LogSample("Approov initialization succeeded. Dev key configured=" + IsDevKeyConfigured());
        return true;
    }

    private ShapesEndpointMetadata GetEndpointMetadata(ShapesEndpointVersion endpointVersion)
    {
        switch (endpointVersion)
        {
            case ShapesEndpointVersion.V1:
                return new ShapesEndpointMetadata
                {
                    Title = "v1",
                    BaseUrl = ShapesHost + "v1/",
                    RequiresApiKey = true,
                    RequiresApproov = false,
                    SupportsMessageSigning = false,
                    Description =
                        "v1 is the baseline API-key-protected endpoint. " +
                        "Hello is public and Shapes succeeds with only the Api-Key header.",
                };
            case ShapesEndpointVersion.V3:
                return new ShapesEndpointMetadata
                {
                    Title = "v3",
                    BaseUrl = ShapesHost + "v3/",
                    RequiresApiKey = true,
                    RequiresApproov = true,
                    SupportsMessageSigning = false,
                    Description =
                        "v3 currently requires both Api-Key and an Approov token for Shapes. " +
                        "Hello remains a public health check.",
                };
            default:
                return new ShapesEndpointMetadata
                {
                    Title = "v5",
                    BaseUrl = ShapesHost + "v5/",
                    RequiresApiKey = true,
                    RequiresApproov = true,
                    SupportsMessageSigning = true,
                    Description =
                        "v5 currently requires both Api-Key and Approov for Shapes. " +
                        "With no signature the backend should reject the request with a message-signature error; install or account signing should only be sent on the protected Shapes call.",
                };
        }
    }

    private UnityWebRequest CreateUnityWebRequest(RequestKind requestKind, ShapesEndpointMetadata metadata)
    {
        UnityWebRequest request = UnityWebRequest.Get(BuildEndpointUrl(metadata, requestKind));
        ApplyCommonHeaders(requestKind, metadata, request.SetRequestHeader);
        return request;
    }

    private HttpRequestMessage CreateHttpRequestMessage(RequestKind requestKind, ShapesEndpointMetadata metadata)
    {
        HttpRequestMessage request = new(HttpMethod.Get, BuildEndpointUrl(metadata, requestKind))
        {
            Version = new Version(1, 1),
        };
        ApplyCommonHeaders(requestKind, metadata, (header, value) => request.Headers.TryAddWithoutValidation(header, value));
        return request;
    }

    private void ApplyCommonHeaders(RequestKind requestKind, ShapesEndpointMetadata metadata, Action<string, string> addHeader)
    {
        addHeader("Accept", "application/json");

        if (requestKind == RequestKind.Shapes && metadata.RequiresApiKey)
        {
            addHeader("Api-Key", ApiKey);
        }
    }

    private string BuildEndpointUrl(ShapesEndpointMetadata metadata, RequestKind requestKind)
    {
        return metadata.BaseUrl + (requestKind == RequestKind.Hello ? "hello/" : "shapes/");
    }

    private void ApplyRequestResult(RequestKind requestKind, SampleRequestResult result)
    {
        if (!result.IsSuccess)
        {
            SelectImage("confused");
            statusText.text = BuildErrorMessage(result);
            return;
        }

        ShapesApiResponse response = TryParseResponse(result.ResponseBody);
        if (response == null || string.IsNullOrWhiteSpace(response.status))
        {
            SelectImage("confused");
            statusText.text = "Unexpected response: " + result.ResponseBody;
            return;
        }

        statusText.text = response.status;
        if (requestKind == RequestKind.Hello || ContainsIgnoreCase(response.status, "hello"))
        {
            SelectImage("hello");
            return;
        }

        SelectImage(ResolveShapeImageName(response));
    }

    private ShapesApiResponse TryParseResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<ShapesApiResponse>(responseBody);
        }
        catch (Exception exception)
        {
            LogSampleWarning("Failed to parse Shapes response: " + exception.Message);
            return null;
        }
    }

    private string BuildErrorMessage(SampleRequestResult result)
    {
        string message = GetResultMessage(result);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "Unknown request error";
        }

        return result.StatusCode > 0
            ? "Error (" + result.StatusCode + "): " + message
            : "Error: " + message;
    }

    private string GetResultMessage(SampleRequestResult result)
    {
        ShapesApiResponse response = TryParseResponse(result?.ResponseBody);
        if (!string.IsNullOrWhiteSpace(response?.status))
        {
            return response.status;
        }

        if (!string.IsNullOrWhiteSpace(result?.Error))
        {
            return result.Error;
        }

        return result?.ResponseBody;
    }

    private SampleRequestResult BuildUnityWebRequestResult(UnityWebRequest request)
    {
        return new SampleRequestResult
        {
            IsSuccess = request.result == UnityWebRequest.Result.Success,
            StatusCode = request.responseCode,
            ResponseBody = request.downloadHandler?.text,
            Error = request.error,
            Diagnostics = BuildUnityResponseDiagnostics(request),
        };
    }

    private static string BuildUnityResponseDiagnostics(UnityWebRequest request)
    {
        StringBuilder builder = new();
        builder.Append(request.method)
            .Append(' ')
            .Append(request.url)
            .Append(" | result=")
            .Append(request.result)
            .Append(" | responseCode=")
            .Append(request.responseCode);

        if (!string.IsNullOrWhiteSpace(request.error))
        {
            builder.Append(" | error=").Append(request.error);
        }

        string body = request.downloadHandler?.text;
        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.Append(" | body=").Append(body);
        }

        return builder.ToString();
    }

    private static string BuildHttpResponseDiagnostics(HttpRequestMessage request, HttpResponseMessage response, string body)
    {
        StringBuilder builder = new();
        builder.Append(request.Method)
            .Append(' ')
            .Append(request.RequestUri)
            .Append(" | version=")
            .Append(response.Version)
            .Append(" | status=")
            .Append((int)response.StatusCode)
            .Append(' ')
            .Append(response.ReasonPhrase);

        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.Append(" | body=").Append(body);
        }

        return builder.ToString();
    }

    private void SelectImage(string imageName)
    {
        string resolvedName = string.IsNullOrWhiteSpace(imageName) ? "confused" : imageName.Trim();
        if (!_imageSprites.TryGetValue(resolvedName, out Sprite sprite))
        {
            sprite = _imageSprites["confused"];
        }

        shapesImage.sprite = sprite;
    }

    private string ResolveShapeImageName(ShapesApiResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response?.shape) && _images.ContainsKey(response.shape.ToLowerInvariant()))
        {
            return response.shape.ToLowerInvariant();
        }

        string status = response?.status ?? string.Empty;
        if (ContainsIgnoreCase(status, "circle"))
        {
            return "circle";
        }

        if (ContainsIgnoreCase(status, "square"))
        {
            return "square";
        }

        if (ContainsIgnoreCase(status, "triangle"))
        {
            return "triangle";
        }

        if (ContainsIgnoreCase(status, "rectangle"))
        {
            return "rectangle";
        }

        return "confused";
    }

    private bool ShouldSignRequest(ApproovRequestContext request)
    {
        if (!IsApproovEnabled() || request?.Uri == null || GetEffectiveSignatureMode() == ShapesSignatureMode.None)
        {
            return false;
        }

        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());
        if (!metadata.SupportsMessageSigning)
        {
            return false;
        }

        string expectedPath = "/" + metadata.Title.ToLowerInvariant() + "/shapes/";
        return string.Equals(request.Uri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRequireApproovToken(ApproovRequestContext request)
    {
        if (!IsApproovEnabled() || request?.Uri == null)
        {
            return false;
        }

        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());
        if (!metadata.RequiresApproov)
        {
            return false;
        }

        string expectedPath = "/" + metadata.Title.ToLowerInvariant() + "/shapes/";
        return string.Equals(request.Uri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleApproovFetchResult(
        ApproovRequestContext request,
        ApproovTokenFetchResult approovResult,
        bool shouldApplyApproovChanges,
        string failureMessage)
    {
        string requestPath = request?.Uri?.AbsoluteUri ?? "(unknown url)";
        string fetchStatus = ApproovService.ApproovTokenFetchStatusToString(approovResult.status);
        string fetchDetails = ApproovService.DescribeFetchResult(approovResult);
        string requestPrefix = _activeRequestId > 0 ? "[req-" + _activeRequestId + "] " : string.Empty;
        string summary =
            requestPrefix +
            "Approov token fetch for " + requestPath +
            " | applyChanges=" + shouldApplyApproovChanges +
            " | requiresToken=" + ShouldRequireApproovToken(request) +
            " | " + fetchDetails;

        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            LogSampleWarning(summary + " | error=" + failureMessage);
            return;
        }

        LogSample(summary);

        if (ShouldRequireApproovToken(request) && !shouldApplyApproovChanges)
        {
            throw new ConfigurationFailureException(
                "Approov did not add a token for a request that this sample expects to be protected. " +
                "Fetch status was " + fetchStatus + ". " +
                "If this is the v3 Shapes request, verify that the v3 path is protected in your Approov configuration.");
        }
    }

    private string GetSignatureSummary(ShapesEndpointMetadata metadata)
    {
        if (!metadata.SupportsMessageSigning)
        {
            return "Signature ignored";
        }

        switch (GetSelectedSignatureMode())
        {
            case ShapesSignatureMode.Install:
                return IsApproovEnabled() ? "Install Signature" : "Install Signature (inactive)";
            case ShapesSignatureMode.Account:
                return IsApproovEnabled() ? "Account Signature" : "Account Signature (inactive)";
            default:
                return "No Signature";
        }
    }

    private ShapesSignatureMode GetEffectiveSignatureMode()
    {
        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());
        return metadata.SupportsMessageSigning ? GetSelectedSignatureMode() : ShapesSignatureMode.None;
    }

    private bool IsApproovEnabled()
    {
        return _approovToggle != null && _approovToggle.isOn;
    }

    private ShapesTransportMode GetSelectedTransport()
    {
        return ValueAt(TransportModes, _transportDropdown.value, defaultTransport);
    }

    internal ShapesEndpointVersion GetSelectedEndpoint()
    {
        return ValueAt(EndpointModes, _endpointDropdown.value, defaultEndpoint);
    }

    internal ShapesSignatureMode GetSelectedSignatureMode()
    {
        return ValueAt(SignatureModes, _signatureDropdown.value, defaultSignatureMode);
    }

    private void UpdateInteractiveState()
    {
        bool interactable = !_isRequestInFlight && !_isAutoTestRunning;

        helloButton.interactable = interactable;
        shapesButton.interactable = interactable;

        if (_autoTestButton != null)
        {
            _autoTestButton.interactable = interactable;
        }

        if (_approovToggle != null)
        {
            _approovToggle.interactable = interactable;
        }

        if (_transportDropdown != null)
        {
            _transportDropdown.interactable = interactable;
        }

        if (_endpointDropdown != null)
        {
            _endpointDropdown.interactable = interactable;
        }

        if (_signatureDropdown != null)
        {
            _signatureDropdown.interactable = interactable && GetEndpointMetadata(GetSelectedEndpoint()).SupportsMessageSigning;
        }
    }

    private void LogOutgoingRequest(int requestId, string prefix, RequestKind requestKind, ShapesEndpointMetadata metadata)
    {
        string message =
            "[req-" + requestId + "] " + prefix + " | " +
            GetRequestKindLabel(requestKind) + " " +
            BuildEndpointUrl(metadata, requestKind) + " | transport=" +
            GetTransportLabel(GetSelectedTransport()) + " | approov=" +
            (IsApproovEnabled() ? "on" : "off") + " | signature=" +
            GetEffectiveSignatureMode() + " | devKey=" +
            IsDevKeyConfigured() + " | expected headers=" +
            BuildExpectedHeaderSummary(requestKind, metadata) + " | config=" +
            BuildConfigurationSnapshot();
        LogSample(message);
    }

    private string BuildExpectedHeaderSummary(RequestKind requestKind, ShapesEndpointMetadata metadata)
    {
        List<string> headers = new();
        if (requestKind == RequestKind.Shapes && metadata.RequiresApiKey)
        {
            headers.Add("Api-Key");
        }

        if (requestKind == RequestKind.Shapes && metadata.RequiresApproov && IsApproovEnabled())
        {
            headers.Add("Approov-Token");
        }

        if (requestKind == RequestKind.Shapes && metadata.SupportsMessageSigning &&
            IsApproovEnabled() && GetEffectiveSignatureMode() != ShapesSignatureMode.None)
        {
            headers.Add(GetEffectiveSignatureMode() + " Signature");
        }

        return headers.Count == 0 ? "none" : string.Join(", ", headers);
    }

    private string BuildConfigurationSnapshot()
    {
        return
            "transport=" + GetSelectedTransport() +
            ", endpoint=" + GetSelectedEndpoint() +
            ", signature=" + GetSelectedSignatureMode() +
            ", approovEnabled=" + IsApproovEnabled() +
            ", sdkInitialized=" + ApproovService.IsSDKInitialized() +
            ", devKeyEnabled=" + IsDevKeyConfigured();
    }

    private void LogRequestResult(int requestId, string prefix, RequestKind requestKind, SampleRequestResult result)
    {
        string summary = "[req-" + requestId + "] " + prefix + " | " + GetRequestKindLabel(requestKind) + " => " +
                         (result.IsSuccess ? "success" : "failure") +
                         " | status=" + result.StatusCode +
                         " | message=" + GetResultMessage(result);

        if (result.IsSuccess)
        {
            LogSample(summary);
        }
        else
        {
            LogSampleWarning(summary);
        }

        if (!string.IsNullOrWhiteSpace(result.Diagnostics))
        {
            Debug.Log("[" + SampleLogTag + "] Diagnostics: " + result.Diagnostics);
        }
    }

    private List<AutoTestScenario> BuildAutoTestScenarios()
    {
        List<AutoTestScenario> scenarios = new();

        for (int approovIndex = 0; approovIndex < ApproovModes.Length; approovIndex++)
        {
            for (int transportIndex = 0; transportIndex < TransportModes.Length; transportIndex++)
            {
                for (int endpointIndex = 0; endpointIndex < EndpointModes.Length; endpointIndex++)
                {
                    bool approovEnabled = ApproovModes[approovIndex];
                    ShapesTransportMode transport = TransportModes[transportIndex];
                    ShapesEndpointVersion endpoint = EndpointModes[endpointIndex];

                    scenarios.Add(CreateAutoTestScenario(
                        approovEnabled,
                        transport,
                        endpoint,
                        ShapesSignatureMode.None,
                        RequestKind.Hello));

                    if (endpoint == ShapesEndpointVersion.V5)
                    {
                        for (int signatureIndex = 0; signatureIndex < SignatureModes.Length; signatureIndex++)
                        {
                            scenarios.Add(CreateAutoTestScenario(
                                approovEnabled,
                                transport,
                                endpoint,
                                SignatureModes[signatureIndex],
                                RequestKind.Shapes));
                        }
                    }
                    else
                    {
                        scenarios.Add(CreateAutoTestScenario(
                            approovEnabled,
                            transport,
                            endpoint,
                            ShapesSignatureMode.None,
                            RequestKind.Shapes));
                    }
                }
            }
        }

        return scenarios;
    }

    private static AutoTestScenario CreateAutoTestScenario(
        bool approovEnabled,
        ShapesTransportMode transport,
        ShapesEndpointVersion endpoint,
        ShapesSignatureMode signatureMode,
        RequestKind requestKind)
    {
        StringBuilder labelBuilder = new();
        labelBuilder.Append(GetTransportLabel(transport))
            .Append(" | ")
            .Append(approovEnabled ? "Approov On" : "Approov Off")
            .Append(" | ")
            .Append(endpoint.ToString().ToLowerInvariant())
            .Append(" | ")
            .Append(GetRequestKindLabel(requestKind));

        if (requestKind == RequestKind.Shapes && endpoint == ShapesEndpointVersion.V5)
        {
            labelBuilder.Append(" | ").Append(signatureMode).Append(" Signature");
        }

        return new AutoTestScenario
        {
            ApproovEnabled = approovEnabled,
            Endpoint = endpoint,
            Label = labelBuilder.ToString(),
            RequestKind = requestKind,
            SignatureMode = signatureMode,
            Transport = transport,
        };
    }

    private bool EvaluateScenario(AutoTestScenario scenario, SampleRequestResult result, out string expectationSummary)
    {
        string actualMessage = GetResultMessage(result) ?? "no message";

        if (scenario.RequestKind == RequestKind.Hello)
        {
            bool passed = result != null && result.IsSuccess && ContainsIgnoreCase(actualMessage, "hello");
            expectationSummary = passed
                ? "Expected hello success and received a healthy hello response."
                : "Expected hello success but got: " + actualMessage;
            return passed;
        }

        switch (scenario.Endpoint)
        {
            case ShapesEndpointVersion.V1:
            {
                bool passed = result != null && result.IsSuccess;
                expectationSummary = passed
                    ? "Expected v1 shape success with Api-Key only."
                    : "Expected v1 shape success but got: " + actualMessage;
                return passed;
            }
            case ShapesEndpointVersion.V3:
            {
                if (scenario.ApproovEnabled)
                {
                    bool passed = result != null && result.IsSuccess;
                    expectationSummary = passed
                        ? "Expected v3 shape success with Approov and Api-Key."
                        : "Expected v3 shape success with Approov and Api-Key but got: " + actualMessage;
                    return passed;
                }

                bool failurePassed = result != null && !result.IsSuccess && ContainsIgnoreCase(actualMessage, "approov token");
                expectationSummary = failurePassed
                    ? "Expected v3 failure because the Approov token is missing."
                    : "Expected a missing Approov token error but got: " + actualMessage;
                return failurePassed;
            }
            default:
            {
                if (!scenario.ApproovEnabled)
                {
                    bool failurePassed = result != null && !result.IsSuccess && ContainsIgnoreCase(actualMessage, "approov token");
                    expectationSummary = failurePassed
                        ? "Expected v5 failure because Approov is disabled."
                        : "Expected a missing Approov token error but got: " + actualMessage;
                    return failurePassed;
                }

                if (scenario.SignatureMode == ShapesSignatureMode.None)
                {
                    bool signatureFailurePassed = result != null && !result.IsSuccess && ContainsIgnoreCase(actualMessage, "message signature");
                    expectationSummary = signatureFailurePassed
                        ? "Expected v5 failure because the request is unsigned."
                        : "Expected a message-signature failure but got: " + actualMessage;
                    return signatureFailurePassed;
                }

                if (scenario.SignatureMode == ShapesSignatureMode.Account)
                {
                    bool accountFailurePassed = result != null && !result.IsSuccess && ContainsIgnoreCase(actualMessage, "message signature");
                    expectationSummary = accountFailurePassed
                        ? "Expected v5 account signing failure because the backend currently rejects account signatures."
                        : "Expected an account-signature failure but got: " + actualMessage;
                    return accountFailurePassed;
                }

                bool successPassed = result != null && result.IsSuccess;
                expectationSummary = successPassed
                    ? "Expected v5 shape success with " + scenario.SignatureMode + " signing."
                    : "Expected v5 shape success with " + scenario.SignatureMode + " signing but got: " + actualMessage;
                return successPassed;
            }
        }
    }

    private static string BuildAutoTestScenarioSummary(AutoTestScenario scenario, string status, string expectationSummary)
    {
        return status + " | " + scenario.Label + " | " + expectationSummary;
    }

    private void ClearScreenLog()
    {
        _screenLogLines.Clear();
        if (_logText != null)
        {
            _logText.text = "No activity yet.";
        }
    }

    private void LogSample(string message)
    {
        Debug.Log("[" + SampleLogTag + "] " + message);
        AppendScreenLog(message);
    }

    private void LogSampleWarning(string message)
    {
        Debug.LogWarning("[" + SampleLogTag + "] " + message);
        AppendScreenLog("WARN: " + message);
    }

    private void LogSampleError(string message)
    {
        Debug.LogError("[" + SampleLogTag + "] " + message);
        AppendScreenLog("ERROR: " + message);
    }

    private void AppendScreenLog(string message)
    {
        if (_logText == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string line = DateTime.Now.ToString("HH:mm:ss") + " " + message;
        _screenLogLines.Add(line);
        if (_screenLogLines.Count > MaxVisibleLogLines)
        {
            _screenLogLines.RemoveAt(0);
        }

        _logText.text = string.Join("\n", _screenLogLines);
    }

    private static string FormatExceptionMessage(Exception exception)
    {
        if (exception == null)
        {
            return null;
        }

        StringBuilder builder = new();
        int depth = 0;
        Exception current = exception;
        while (current != null && depth < 4)
        {
            if (depth > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(current.GetType().Name)
                .Append(": ")
                .Append(current.Message);
            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    private static bool IsApproovRuntimeSupported()
    {
        return Application.platform == RuntimePlatform.Android ||
               Application.platform == RuntimePlatform.IPhonePlayer;
    }

    private static bool ContainsIgnoreCase(string value, string expected)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetRequestKindLabel(RequestKind requestKind)
    {
        return requestKind == RequestKind.Hello ? "Hello" : "Shapes";
    }

    private static string GetTransportLabel(ShapesTransportMode transportMode)
    {
        return transportMode == ShapesTransportMode.HttpClient ? "HttpClient" : "UnityWebRequest";
    }

    private int NextRequestId()
    {
        _requestSequence++;
        return _requestSequence;
    }

    private static int IndexOf<T>(IReadOnlyList<T> values, T target)
    {
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int index = 0; index < values.Count; index++)
        {
            if (comparer.Equals(values[index], target))
            {
                return index;
            }
        }

        return 0;
    }

    private static T ValueAt<T>(IReadOnlyList<T> values, int index, T fallback)
    {
        return index >= 0 && index < values.Count ? values[index] : fallback;
    }
}
