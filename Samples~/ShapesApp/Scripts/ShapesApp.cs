using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Approov;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ShapesApp : MonoBehaviour
{
    private const string ShapesHost = "https://shapes.approov.io/";
    private const string ApiKey = "yXClypapWNHIifHUWmBIyPFAm";

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
        public string Error;
        public bool IsSuccess;
        public string ResponseBody;
        public long StatusCode;
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
            return ApproovServiceMutator.Default.HandleInterceptorFetchTokenResult(request, approovResult);
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

    private readonly Dictionary<string, Texture2D> _images = new(StringComparer.OrdinalIgnoreCase);
    private Text _currentConfigText;
    private Text _endpointDescriptionText;
    private bool _isRequestInFlight;
    private ShapesMessageSigningMutator _messageSigningMutator;
    private string _approovInitializationError;
    private Dropdown _endpointDropdown;
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

        helloButton.onClick.AddListener(OnHelloButtonClicked);
        shapesButton.onClick.AddListener(OnShapesButtonClicked);

        ConfigureBaseLayout();
        CreateRuntimeControls();
        ApplyDefaultSelections();

        SelectImage("approov");
        statusText.text = "Choose a transport and endpoint, then press Hello or Get Shape.";

        RefreshConfigurationUi();
        ApplyMutatorConfiguration();
    }

    private void OnDestroy()
    {
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

    private void StartRequest(RequestKind requestKind)
    {
        if (_isRequestInFlight)
        {
            return;
        }

        if (IsApproovEnabled() && !EnsureApproovInitialized())
        {
            string initError = string.IsNullOrWhiteSpace(_approovInitializationError)
                ? "Approov initialization did not complete successfully."
                : _approovInitializationError;
            statusText.text = "Approov initialization failed: " + initError;
            SelectImage("confused");
            return;
        }

        ApplyMutatorConfiguration();
        BeginRequest(requestKind);

        if (GetSelectedTransport() == ShapesTransportMode.HttpClient)
        {
            _ = SendHttpClientRequestAsync(requestKind);
        }
        else
        {
            StartCoroutine(SendUnityWebRequestCoroutine(requestKind));
        }
    }

    private void BeginRequest(RequestKind requestKind)
    {
        _isRequestInFlight = true;
        SetInteractiveState(false);
        statusText.text = "Sending " + GetRequestKindLabel(requestKind) + " via " + GetTransportLabel(GetSelectedTransport()) + "...";
    }

    private void EndRequest()
    {
        _isRequestInFlight = false;
        SetInteractiveState(true);
        RefreshConfigurationUi();
    }

    private IEnumerator SendUnityWebRequestCoroutine(RequestKind requestKind)
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
            ApplyRequestResult(requestKind, new SampleRequestResult
            {
                IsSuccess = false,
                Error = exception.Message,
            });
            EndRequest();
            yield break;
        }

        yield return operation;

        SampleRequestResult result = BuildUnityWebRequestResult(request);
        request.Dispose();

        ApplyRequestResult(requestKind, result);
        EndRequest();
    }

    private async Task SendHttpClientRequestAsync(RequestKind requestKind)
    {
        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());
        SampleRequestResult result;

        try
        {
            using HttpClient client = IsApproovEnabled()
                ? ApproovService.CreateHttpClient()
                : new HttpClient();
            using HttpRequestMessage request = CreateHttpRequestMessage(requestKind, metadata);
            using HttpResponseMessage response = await client.SendAsync(request);
            string body = response.Content == null ? null : await response.Content.ReadAsStringAsync();
            result = new SampleRequestResult
            {
                IsSuccess = response.IsSuccessStatusCode,
                StatusCode = (long)response.StatusCode,
                ResponseBody = body,
                Error = response.IsSuccessStatusCode ? null : response.ReasonPhrase,
            };
        }
        catch (Exception exception)
        {
            result = new SampleRequestResult
            {
                IsSuccess = false,
                Error = exception.Message,
                ResponseBody = null,
                StatusCode = 0,
            };
        }

        ApplyRequestResult(requestKind, result);
        EndRequest();
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
        foreach (Texture2D texture in textures)
        {
            if (texture != null && !_images.ContainsKey(texture.name))
            {
                _images.Add(texture.name, texture);
            }
        }

        return _images.Count > 0;
    }

    private void ConfigureBaseLayout()
    {
        RectTransform imageRect = shapesImage.rectTransform;
        imageRect.anchorMin = new Vector2(0.5f, 1f);
        imageRect.anchorMax = new Vector2(0.5f, 1f);
        imageRect.pivot = new Vector2(0.5f, 1f);
        imageRect.anchoredPosition = new Vector2(0f, -270f);
        imageRect.sizeDelta = new Vector2(220f, 180f);

        RectTransform statusRect = statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 1f);
        statusRect.anchorMax = new Vector2(0.5f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.anchoredPosition = new Vector2(0f, -480f);
        statusRect.sizeDelta = new Vector2(360f, 72f);
        statusText.alignment = TextAnchor.UpperCenter;

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
        panelRect.sizeDelta = new Vector2(360f, 230f);

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
        _endpointDescriptionText = CreatePanelText(panelObject.transform, 13, FontStyle.Normal, 60f);
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
        _approovToggle.isOn = defaultApproovEnabled;
        _transportDropdown.value = (int)defaultTransport;
        _endpointDropdown.value = (int)defaultEndpoint;
        _signatureDropdown.value = (int)defaultSignatureMode;
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

        LayoutElement layoutElement = toggleObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 28f;
        return toggle;
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
        for (int i = 0; i < options.Count; i++)
        {
            dropdown.options.Add(new Dropdown.OptionData(options[i]));
        }

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.onValueChanged.AddListener(value => onChanged(value));

        LayoutElement dropdownLayout = dropdownObject.AddComponent<LayoutElement>();
        dropdownLayout.preferredWidth = 190f;
        dropdownLayout.flexibleWidth = 1f;

        foreach (Text text in dropdownObject.GetComponentsInChildren<Text>(true))
        {
            text.font = statusText.font;
            text.fontSize = 13;
            text.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        }

        return dropdown;
    }

    private void OnApproovToggleChanged(bool _)
    {
        ApplyMutatorConfiguration();
        RefreshConfigurationUi();
    }

    private void OnTransportDropdownChanged(int _)
    {
        RefreshConfigurationUi();
    }

    private void OnEndpointDropdownChanged(int _)
    {
        ApplyMutatorConfiguration();
        RefreshConfigurationUi();
    }

    private void OnSignatureDropdownChanged(int _)
    {
        ApplyMutatorConfiguration();
        RefreshConfigurationUi();
    }

    private void RefreshConfigurationUi()
    {
        ShapesEndpointMetadata metadata = GetEndpointMetadata(GetSelectedEndpoint());
        _endpointDescriptionText.text = metadata.Description;
        _signatureDropdown.interactable = !_isRequestInFlight && metadata.SupportsMessageSigning;

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

        if (GetEffectiveSignatureMode() == ShapesSignatureMode.None)
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
            _approovInitializationError = null;
            return true;
        }

        try
        {
            ApproovService.Initialize();
        }
        catch (Exception exception)
        {
            _approovInitializationError = exception.Message;
            Debug.LogWarning("Shapes sample failed to initialize Approov: " + exception.Message);
            return false;
        }

        if (!ApproovService.IsSDKInitialized())
        {
            _approovInitializationError =
                "Approov remains unavailable on this platform. Build for Android or iOS with a valid config string.";
            return false;
        }

        _approovInitializationError = null;
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
                        "v1 demonstrates the baseline API-key-protected Shapes endpoint. " +
                        "Hello is public. Shapes requires the Api-Key header and succeeds without Approov.",
                };
            case ShapesEndpointVersion.V3:
                return new ShapesEndpointMetadata
                {
                    Title = "v3",
                    BaseUrl = ShapesHost + "v3/",
                    RequiresApiKey = false,
                    RequiresApproov = true,
                    SupportsMessageSigning = false,
                    Description =
                        "v3 demonstrates Approov token enforcement. " +
                        "Hello remains a public health check. Shapes requires an Approov token and does not use Api-Key.",
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
                        "v5 demonstrates the signature-validation flow. " +
                        "Shapes sends Api-Key plus an Approov token, and this sample can add install or account message signatures on the protected Shapes request.",
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
        HttpRequestMessage request = new(HttpMethod.Get, BuildEndpointUrl(metadata, requestKind));
        ApplyCommonHeaders(requestKind, metadata, (header, value) => request.Headers.TryAddWithoutValidation(header, value));
        return request;
    }

    private void ApplyCommonHeaders(RequestKind requestKind, ShapesEndpointMetadata metadata, Action<string, string> addHeader)
    {
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
            Debug.LogWarning("Shapes sample failed to parse response: " + exception.Message);
            return null;
        }
    }

    private string BuildErrorMessage(SampleRequestResult result)
    {
        ShapesApiResponse response = TryParseResponse(result.ResponseBody);
        string message = !string.IsNullOrWhiteSpace(response?.status)
            ? response.status
            : !string.IsNullOrWhiteSpace(result.ResponseBody)
                ? result.ResponseBody
                : result.Error;

        if (string.IsNullOrWhiteSpace(message))
        {
            message = "Unknown request error";
        }

        return result.StatusCode > 0
            ? "Error (" + result.StatusCode + "): " + message
            : "Error: " + message;
    }

    private SampleRequestResult BuildUnityWebRequestResult(UnityWebRequest request)
    {
        return new SampleRequestResult
        {
            IsSuccess = request.result == UnityWebRequest.Result.Success,
            StatusCode = request.responseCode,
            ResponseBody = request.downloadHandler?.text,
            Error = request.error,
        };
    }

    private void SelectImage(string imageName)
    {
        string resolvedName = string.IsNullOrWhiteSpace(imageName) ? "confused" : imageName.Trim();
        if (!_images.TryGetValue(resolvedName, out Texture2D texture))
        {
            texture = _images["confused"];
        }

        shapesImage.sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));
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
        return (ShapesTransportMode)_transportDropdown.value;
    }

    internal ShapesEndpointVersion GetSelectedEndpoint()
    {
        return (ShapesEndpointVersion)_endpointDropdown.value;
    }

    internal ShapesSignatureMode GetSelectedSignatureMode()
    {
        return (ShapesSignatureMode)_signatureDropdown.value;
    }

    private void SetInteractiveState(bool interactable)
    {
        helloButton.interactable = interactable;
        shapesButton.interactable = interactable;

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
}
