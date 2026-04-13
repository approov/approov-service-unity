using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Approov;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MessageSigningHarnessApp : MonoBehaviour
{
    private const string DefaultWorkerUrl = "";
    private const string DiagnosticsFileName = "approov-message-signing-harness.log";
    private const string SampleLogTag = "MessageSigningHarness";
    private const int HttpTimeoutSeconds = 20;
    private const int MaxVisibleLogLines = 10;
    private const int DefaultRandomHeaderCount = 3;
    private const int MaxBatchRunCount = 500;
    private const string RandomHeadersListHeader = "X-Harness-Random-Headers";
    private const string RequestIdHeader = "X-Harness-Request-Id";

    private static readonly bool[] ApproovModes =
    {
        false,
        true
    };

    private static readonly HarnessTransportMode[] TransportModes =
    {
        HarnessTransportMode.UnityWebRequest,
        HarnessTransportMode.HttpClient
    };

    private static readonly HarnessSignatureMode[] SignatureModes =
    {
        HarnessSignatureMode.None,
        HarnessSignatureMode.Install
    };

    public enum HarnessTransportMode
    {
        UnityWebRequest,
        HttpClient
    }

    public enum HarnessSignatureMode
    {
        None,
        Install
    }

    [Serializable]
    private sealed class WorkerVerificationDetails
    {
        public string tokenResult;
        public string messageSigningMode;
        public string messageSigningResult;
        public string bindingResult;
    }

    [Serializable]
    private sealed class WorkerVerificationResponse
    {
        public bool ok;
        public string status;
        public string reason;
        public WorkerVerificationDetails details;
    }

    private sealed class SampleRequestResult
    {
        public string Diagnostics;
        public string Error;
        public bool IsSuccess;
        public RequestPlan Plan;
        public WorkerVerificationResponse Verification;
        public string ResponseBody;
        public long StatusCode;
    }

    private sealed class RequestPlan
    {
        public string RequestId;
        public List<System.Collections.Generic.KeyValuePair<string, string>> Headers = new();
        public List<string> RandomHeaderNames = new();
        public string Summary;
    }

    private sealed class AutoTestScenario
    {
        public bool ApproovEnabled;
        public string Label;
        public HarnessSignatureMode SignatureMode;
        public HarnessTransportMode Transport;
    }

    private sealed class InstallMessageSigningMutator : ApproovServiceMutator
    {
        private readonly MessageSigningHarnessApp _owner;

        public InstallMessageSigningMutator(MessageSigningHarnessApp owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public override string ToString()
        {
            return "InstallMessageSigningMutator";
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

            string[] dynamicHeaderNames = _owner.GetDynamicSignedHeaders(request);
            ApproovDefaultMessageSigning signer = new ApproovDefaultMessageSigning()
                .SetDefaultFactory(
                    ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory()
                        .SetUseInstallMessageSigning()
                        .AddOptionalHeaders(dynamicHeaderNames));
            signer.HandleProcessedRequest(request, changes);
        }
    }

    public Button helloButton;
    public Button shapesButton;
    public Text statusText;
    public Image shapesImage;

    [SerializeField] private string defaultWorkerUrl = DefaultWorkerUrl;
    [SerializeField] private bool defaultApproovEnabled = true;
    [SerializeField] private HarnessTransportMode defaultTransport = HarnessTransportMode.UnityWebRequest;
    [SerializeField] private HarnessSignatureMode defaultSignatureMode = HarnessSignatureMode.Install;
    [SerializeField] private bool enableDetailedServiceLogging = true;
    [SerializeField] private bool useApproovDevKey = false;
    [SerializeField] private string approovDevKey = string.Empty;
    [SerializeField] private int defaultRunCount = 1;

    private readonly Dictionary<string, Sprite> _imageSprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture2D> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _persistentLogLock = new();
    private readonly List<string> _screenLogLines = new();

    private int _activeRequestId;
    private Button _autoTestButton;
    private string _approovInitializationError;
    private Text _currentConfigText;
    private bool _isAutoTestRunning;
    private bool _isRequestInFlight;
    private Text _instructionsText;
    private string _logFilePath;
    private Text _logText;
    private InstallMessageSigningMutator _messageSigningMutator;
    private int _requestSequence;
    private Toggle _approovToggle;
    private Dropdown _signatureDropdown;
    private Dropdown _transportDropdown;
    private InputField _workerUrlInput;
    private InputField _runCountInput;

    private void Start()
    {
        if (!LoadImageResources())
        {
            throw new Exception("Failed to load message-signing harness images.");
        }

        if (helloButton == null || shapesButton == null || statusText == null || shapesImage == null)
        {
            throw new InvalidOperationException("Message signing harness scene is missing one or more required UI references.");
        }

        InitializePersistentLogging();
        ApproovService.SetDetailedDebugLogging(enableDetailedServiceLogging);
        LogConfiguredDevKeyState();

        helloButton.onClick.AddListener(OnCallWorkerClicked);
        shapesButton.onClick.AddListener(OnRunCurrentTestClicked);

        ConfigureBaseLayout();
        CreateRuntimeControls();
        ApplyDefaultSelections();
        ApplyButtonLabels();

        SelectImage("approov");
        statusText.text = "Configure the worker URL, then call the worker or run the current test.";

        ClearScreenLog();
        LogSample("Harness started on " + Application.platform + ". Detailed service logging is " +
                  (enableDetailedServiceLogging ? "enabled." : "disabled."));
        LogSample("Persistent diagnostics log: " + _logFilePath);

        RefreshConfigurationUi();
        ApplyMutatorConfiguration();
    }

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= HandleUnityLogMessage;
        ApproovService.SetServiceMutator(ApproovServiceMutator.Default);
    }

    private void ApplyButtonLabels()
    {
        SetButtonLabel(helloButton, "Call Worker");
        SetButtonLabel(shapesButton, "Run Current Test");
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null)
        {
            return;
        }

        Text buttonText = button.GetComponentInChildren<Text>(true);
        if (buttonText != null)
        {
            buttonText.text = label;
        }
    }

    private void LogConfiguredDevKeyState()
    {
        if (!IsDevKeyConfigured())
        {
            LogSample("Approov dev key disabled for this harness build.");
            return;
        }

        LogSampleWarning("Approov dev key configured for this harness build and will be applied after SDK initialization. " +
                         "Do not ship a production app with a dev key because it bypasses normal attestation checks.");
    }

    private void ApplyConfiguredDevKey()
    {
        if (!IsDevKeyConfigured())
        {
            return;
        }

        ApproovService.SetDevKey(approovDevKey);
        LogSampleWarning("Approov dev key configured for this harness build. sdkInitialized=" + ApproovService.IsSDKInitialized() +
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
                "===== Message signing harness diagnostics session " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") +
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

    public void OnCallWorkerClicked()
    {
        StartRequest(validateExpectations: false);
    }

    public void OnRunCurrentTestClicked()
    {
        StartRequest(validateExpectations: true);
    }

    private void OnAutoTestButtonClicked()
    {
        if (_isRequestInFlight || _isAutoTestRunning)
        {
            return;
        }

        StartCoroutine(RunAutoTestCoroutine());
    }

    private void StartRequest(bool validateExpectations)
    {
        if (_isRequestInFlight || _isAutoTestRunning)
        {
            return;
        }

        if (validateExpectations)
        {
            StartCoroutine(RunCurrentScenarioBatchCoroutine());
            return;
        }

        StartCoroutine(RunInteractiveRequestCoroutine());
    }

    private IEnumerator RunInteractiveRequestCoroutine()
    {
        int requestId = NextRequestId();
        if (!TryGetWorkerUri(out Uri workerUri, out string workerUrlError))
        {
            string message = "Worker URL is invalid: " + workerUrlError;
            statusText.text = message;
            SelectImage("confused");
            LogSampleWarning(message);
            yield break;
        }

        if (!PrepareRequestForExecution(out string initError))
        {
            string message = "Approov initialization failed: " + initError;
            statusText.text = message;
            SelectImage("confused");
            LogSampleWarning(message);
            yield break;
        }

        RequestPlan requestPlan = BuildRequestPlan();
        string contextLabel = "Calling worker";

        BeginRequest(requestId, contextLabel);
        LogOutgoingRequest(requestId, "Manual request", BuildCurrentScenario(), workerUri, requestPlan);

        SampleRequestResult result = null;
        yield return StartCoroutine(ExecuteCurrentRequestCoroutine(workerUri, requestPlan, value => result = value));

        ApplyRawRequestResult(result);
        LogRequestResult(requestId, "Manual request complete", result);
        EndRequest();
    }

    private IEnumerator RunCurrentScenarioBatchCoroutine()
    {
        int runCount = GetRunCount();
        if (!TryGetWorkerUri(out Uri workerUri, out string workerUrlError))
        {
            string message = "Worker URL is invalid: " + workerUrlError;
            statusText.text = message;
            SelectImage("confused");
            LogSampleWarning(message);
            yield break;
        }

        if (!PrepareRequestForExecution(out string initError))
        {
            string message = "Approov initialization failed: " + initError;
            statusText.text = message;
            SelectImage("confused");
            LogSampleWarning(message);
            yield break;
        }

        AutoTestScenario scenario = BuildCurrentScenario();
        _isAutoTestRunning = true;
        UpdateInteractiveState();
        ClearScreenLog();
        SelectImage("approov");
        statusText.text = "Running current scenario " + runCount + " times...";

        int passed = 0;
        int failed = 0;

        for (int runIndex = 0; runIndex < runCount; runIndex++)
        {
            int requestId = NextRequestId();
            RequestPlan requestPlan = BuildRequestPlan();

            BeginRequest(requestId, "[" + (runIndex + 1) + "/" + runCount + "]");
            LogOutgoingRequest(requestId, "Current scenario run " + (runIndex + 1), scenario, workerUri, requestPlan);

            SampleRequestResult result = null;
            yield return StartCoroutine(ExecuteCurrentRequestCoroutine(workerUri, requestPlan, value => result = value));

            bool scenarioPassed = EvaluateScenario(scenario, result, out string expectationSummary);
            if (scenarioPassed)
            {
                passed++;
                SelectImage("hello");
                LogSample("PASS | run " + (runIndex + 1) + "/" + runCount + " | " + expectationSummary);
            }
            else
            {
                failed++;
                SelectImage("confused");
                LogSampleError("FAIL | run " + (runIndex + 1) + "/" + runCount + " | " + expectationSummary);
            }

            statusText.text = "Run " + (runIndex + 1) + "/" + runCount + " | " + (scenarioPassed ? "PASS" : "FAIL");
            LogRequestResult(requestId, "Current scenario run complete", result);
            EndRequest();
            yield return null;
        }

        _isAutoTestRunning = false;
        UpdateInteractiveState();
        RefreshConfigurationUi();

        string finalSummary = "Current scenario batch complete: " + passed + " passed, " + failed + " failed, runCount=" + runCount + ".";
        statusText.text = finalSummary;
        LogSample(finalSummary);
    }

    private IEnumerator RunAutoTestCoroutine()
    {
        bool previousApproovEnabled = IsApproovEnabled();
        HarnessTransportMode previousTransport = GetSelectedTransport();
        HarnessSignatureMode previousSignature = GetSelectedSignatureMode();

        if (!TryGetWorkerUri(out Uri workerUri, out string workerUrlError))
        {
            string message = "Worker URL is invalid: " + workerUrlError;
            statusText.text = message;
            SelectImage("confused");
            LogSampleWarning(message);
            yield break;
        }

        _isAutoTestRunning = true;
        UpdateInteractiveState();
        ClearScreenLog();
        SelectImage("approov");
        statusText.text = "Running message-signing harness scenarios...";

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

            BeginRequest(requestId, "[" + (index + 1) + "/" + scenarios.Count + "]");
            RequestPlan requestPlan = BuildRequestPlan();
            LogOutgoingRequest(requestId, "Scenario " + (index + 1) + "/" + scenarios.Count, scenario, workerUri, requestPlan);

            SampleRequestResult result = null;
            yield return StartCoroutine(ExecuteCurrentRequestCoroutine(workerUri, requestPlan, value => result = value));

            bool scenarioPassed = EvaluateScenario(scenario, result, out string expectationSummary);
            string autoTestSummary = BuildAutoTestScenarioSummary(
                scenario,
                scenarioPassed ? "PASS" : "FAIL",
                expectationSummary);

            if (scenarioPassed)
            {
                passed++;
                LogSample(autoTestSummary);
                SelectImage("hello");
            }
            else
            {
                failed++;
                LogSampleError(autoTestSummary);
                SelectImage("confused");
            }

            statusText.text = autoTestSummary;
            EndRequest();
            yield return null;
        }

        SetSelectionsWithoutNotify(previousApproovEnabled, previousTransport, previousSignature);
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

    private void BeginRequest(int requestId, string contextLabel)
    {
        _activeRequestId = requestId;
        _isRequestInFlight = true;
        UpdateInteractiveState();

        string prefix = string.IsNullOrWhiteSpace(contextLabel) ? string.Empty : contextLabel + " ";
        statusText.text = prefix + "[req-" + requestId + "] Sending " + GetTransportLabel(GetSelectedTransport()) + " request...";
    }

    private void EndRequest()
    {
        _activeRequestId = 0;
        _isRequestInFlight = false;
        UpdateInteractiveState();
        RefreshConfigurationUi();
    }

    private IEnumerator ExecuteCurrentRequestCoroutine(Uri workerUri, RequestPlan requestPlan, Action<SampleRequestResult> onComplete)
    {
        SampleRequestResult result = null;
        if (GetSelectedTransport() == HarnessTransportMode.HttpClient)
        {
            Task<SampleRequestResult> task = SendHttpClientRequestAsync(workerUri, requestPlan);
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
            yield return StartCoroutine(SendUnityWebRequestCoroutine(workerUri, requestPlan, value => result = value));
        }

        onComplete?.Invoke(result ?? new SampleRequestResult
        {
            IsSuccess = false,
            Error = "No request result was produced.",
        });
    }

    private IEnumerator SendUnityWebRequestCoroutine(Uri workerUri, RequestPlan requestPlan, Action<SampleRequestResult> onComplete)
    {
        UnityWebRequest request = CreateUnityWebRequest(workerUri, requestPlan);
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
        result.Plan = requestPlan;
        request.Dispose();
        onComplete?.Invoke(result);
    }

    private async Task<SampleRequestResult> SendHttpClientRequestAsync(Uri workerUri, RequestPlan requestPlan)
    {
        try
        {
            using HttpClient client = CreateHarnessHttpClient();
            using HttpRequestMessage request = CreateHttpRequestMessage(workerUri, requestPlan);
            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string body = response.Content == null ? null : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return new SampleRequestResult
            {
                IsSuccess = response.IsSuccessStatusCode,
                StatusCode = (long)response.StatusCode,
                ResponseBody = body,
                Error = response.IsSuccessStatusCode ? null : response.ReasonPhrase,
                Diagnostics = BuildHttpResponseDiagnostics(request, response, body),
                Plan = requestPlan,
                Verification = TryParseWorkerResponse(body),
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

    private HttpClient CreateHarnessHttpClient()
    {
        HttpClientHandler handler = new();
        HttpClient client = IsApproovEnabled()
            ? ApproovService.CreateHttpClient(handler)
            : new HttpClient(handler, disposeHandler: true);
        client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);
        return client;
    }

    private UnityWebRequest CreateUnityWebRequest(Uri workerUri, RequestPlan requestPlan)
    {
        UnityWebRequest request = UnityWebRequest.Get(workerUri.AbsoluteUri);
        ApplyPlanHeaders(requestPlan, request.SetRequestHeader);
        return request;
    }

    private HttpRequestMessage CreateHttpRequestMessage(Uri workerUri, RequestPlan requestPlan)
    {
        HttpRequestMessage request = new(HttpMethod.Get, workerUri)
        {
            Version = new Version(1, 1),
        };
        ApplyPlanHeaders(requestPlan, (header, value) => request.Headers.TryAddWithoutValidation(header, value));
        return request;
    }

    private void ApplyPlanHeaders(RequestPlan requestPlan, Action<string, string> addHeader)
    {
        for (int index = 0; index < requestPlan.Headers.Count; index++)
        {
            System.Collections.Generic.KeyValuePair<string, string> header = requestPlan.Headers[index];
            addHeader(header.Key, header.Value);
        }
    }

    private SampleRequestResult BuildUnityWebRequestResult(UnityWebRequest request)
    {
        string body = request.downloadHandler?.text;
        return new SampleRequestResult
        {
            IsSuccess = request.result == UnityWebRequest.Result.Success,
            StatusCode = request.responseCode,
            ResponseBody = body,
            Error = request.error,
            Diagnostics = BuildUnityResponseDiagnostics(request),
            Verification = TryParseWorkerResponse(body),
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

    private void ApplyRequestResult(SampleRequestResult result, bool validateExpectations, AutoTestScenario scenario)
    {
        if (!validateExpectations)
        {
            ApplyRawRequestResult(result);
            return;
        }

        bool passed = EvaluateScenario(scenario, result, out string expectationSummary);
        statusText.text = (passed ? "PASS: " : "FAIL: ") + expectationSummary;
        if (passed)
        {
            SelectImage("hello");
            LogSample("Current test passed. " + expectationSummary);
        }
        else
        {
            SelectImage("confused");
            LogSampleError("Current test failed. " + expectationSummary);
        }
    }

    private void ApplyRawRequestResult(SampleRequestResult result)
    {
        if (result?.Verification != null)
        {
            statusText.text = result.Verification.status + ": " + result.Verification.reason;
            SelectImage(result.Verification.ok ? "hello" : "confused");
            return;
        }

        string message = BuildErrorMessage(result);
        statusText.text = string.IsNullOrWhiteSpace(message) ? "Worker call completed with no structured response." : message;
        SelectImage(result != null && result.IsSuccess ? "hello" : "confused");
    }

    private static string BuildErrorMessage(SampleRequestResult result)
    {
        if (!string.IsNullOrWhiteSpace(result?.Verification?.reason))
        {
            return result.Verification.reason;
        }

        if (!string.IsNullOrWhiteSpace(result?.Error))
        {
            return result.Error;
        }

        return result?.ResponseBody;
    }

    private bool EvaluateScenario(AutoTestScenario scenario, SampleRequestResult result, out string expectationSummary)
    {
        if (result == null)
        {
            expectationSummary = "No request result was produced.";
            return false;
        }

        WorkerVerificationResponse verification = result.Verification;
        if (verification == null)
        {
            expectationSummary = "Worker response was not valid JSON: " + (result.ResponseBody ?? result.Error ?? "empty body");
            return false;
        }

        string tokenResult = verification.details?.tokenResult;
        string messageSigningResult = verification.details?.messageSigningResult;
        string messageSigningMode = verification.details?.messageSigningMode;

        if (!scenario.ApproovEnabled)
        {
            bool passed = !verification.ok &&
                          string.Equals(tokenResult, "MISSING_HEADER", StringComparison.OrdinalIgnoreCase);
            expectationSummary = passed
                ? "Expected a missing Approov token header and the worker returned that exact result."
                : "Expected MISSING_HEADER for the Approov token but got tokenResult=" + tokenResult + ", reason=" + verification.reason;
            return passed;
        }

        if (scenario.SignatureMode == HarnessSignatureMode.None)
        {
            bool passed = !verification.ok &&
                          string.Equals(tokenResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(messageSigningResult, "MISSING_HEADERS", StringComparison.OrdinalIgnoreCase);
            expectationSummary = passed
                ? "Expected an unsigned request to fail with MISSING_HEADERS after token validation passed."
                : "Expected token PASS plus MISSING_HEADERS but got tokenResult=" + tokenResult + ", messageSigningResult=" + messageSigningResult + ", reason=" + verification.reason;
            return passed;
        }

        bool successPassed = verification.ok &&
                             string.Equals(tokenResult, "PASS", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(messageSigningMode, "install", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(messageSigningResult, "VALID", StringComparison.OrdinalIgnoreCase);
        expectationSummary = successPassed
            ? "Expected install message signing to validate and the worker returned PASS/VALID for requestId=" + result?.Plan?.RequestId + "."
            : "Expected token PASS and install VALID but got tokenResult=" + tokenResult + ", messageSigningMode=" + messageSigningMode + ", messageSigningResult=" + messageSigningResult + ", reason=" + verification.reason + ", requestId=" + result?.Plan?.RequestId;
        return successPassed;
    }

    private List<AutoTestScenario> BuildAutoTestScenarios()
    {
        List<AutoTestScenario> scenarios = new();
        for (int approovIndex = 0; approovIndex < ApproovModes.Length; approovIndex++)
        {
            for (int transportIndex = 0; transportIndex < TransportModes.Length; transportIndex++)
            {
                for (int signatureIndex = 0; signatureIndex < SignatureModes.Length; signatureIndex++)
                {
                    bool approovEnabled = ApproovModes[approovIndex];
                    HarnessTransportMode transport = TransportModes[transportIndex];
                    HarnessSignatureMode signature = SignatureModes[signatureIndex];

                    scenarios.Add(new AutoTestScenario
                    {
                        ApproovEnabled = approovEnabled,
                        Transport = transport,
                        SignatureMode = signature,
                        Label = BuildScenarioLabel(approovEnabled, transport, signature),
                    });
                }
            }
        }

        return scenarios;
    }

    private static string BuildAutoTestScenarioSummary(AutoTestScenario scenario, string status, string expectationSummary)
    {
        return status + " | " + scenario.Label + " | " + expectationSummary;
    }

    private static string BuildScenarioLabel(bool approovEnabled, HarnessTransportMode transport, HarnessSignatureMode signature)
    {
        return GetTransportLabel(transport) + " | " +
               (approovEnabled ? "Approov On" : "Approov Off") + " | " +
               signature + " Signature";
    }

    private AutoTestScenario BuildCurrentScenario()
    {
        return new AutoTestScenario
        {
            ApproovEnabled = IsApproovEnabled(),
            Transport = GetSelectedTransport(),
            SignatureMode = GetSelectedSignatureMode(),
            Label = BuildScenarioLabel(IsApproovEnabled(), GetSelectedTransport(), GetSelectedSignatureMode()),
        };
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

    private void ApplyMutatorConfiguration()
    {
        if (!IsApproovEnabled() || !ApproovService.IsSDKInitialized() || GetSelectedSignatureMode() == HarnessSignatureMode.None)
        {
            ApproovService.SetServiceMutator(ApproovServiceMutator.Default);
            return;
        }

        _messageSigningMutator ??= new InstallMessageSigningMutator(this);
        ApproovService.SetServiceMutator(_messageSigningMutator);
    }

    private bool ShouldSignRequest(ApproovRequestContext request)
    {
        if (!IsApproovEnabled() || request?.Uri == null || GetSelectedSignatureMode() != HarnessSignatureMode.Install)
        {
            return false;
        }

        if (!TryGetWorkerUri(out Uri workerUri, out _))
        {
            return false;
        }

        return IsHarnessRequest(workerUri, request.Uri);
    }

    internal string[] GetDynamicSignedHeaders(ApproovRequestContext request)
    {
        string headerList = request?.GetHeader(RandomHeadersListHeader);
        if (string.IsNullOrWhiteSpace(headerList))
        {
            return Array.Empty<string>();
        }

        string[] names = headerList.Split(',');
        List<string> result = new();
        for (int index = 0; index < names.Length; index++)
        {
            string trimmed = names[index]?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result.ToArray();
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
            " | " + fetchDetails;

        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            LogSampleWarning(summary + " | error=" + failureMessage);
            return;
        }

        LogSample(summary);

        if (request?.Uri != null && IsApproovEnabled() && !shouldApplyApproovChanges && IsHarnessRequest(request.Uri))
        {
            throw new ConfigurationFailureException(
                "Approov did not add a token for the worker request. " +
                "Fetch status was " + fetchStatus + ".");
        }
    }

    private bool IsHarnessRequest(Uri requestUri)
    {
        return TryGetWorkerUri(out Uri workerUri, out _) && IsHarnessRequest(workerUri, requestUri);
    }

    private static bool IsHarnessRequest(Uri workerUri, Uri requestUri)
    {
        if (workerUri == null || requestUri == null)
        {
            return false;
        }

        return string.Equals(workerUri.Scheme, requestUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(workerUri.Authority, requestUri.Authority, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(workerUri.AbsolutePath, requestUri.AbsolutePath, StringComparison.Ordinal);
    }

    private WorkerVerificationResponse TryParseWorkerResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            WorkerVerificationResponse response = JsonUtility.FromJson<WorkerVerificationResponse>(responseBody);
            return string.IsNullOrWhiteSpace(response?.status) && string.IsNullOrWhiteSpace(response?.reason)
                ? null
                : response;
        }
        catch (Exception exception)
        {
            LogSampleWarning("Failed to parse worker response: " + exception.Message);
            return null;
        }
    }

    private bool TryGetWorkerUri(out Uri workerUri, out string error)
    {
        string workerUrl = _workerUrlInput == null ? defaultWorkerUrl : _workerUrlInput.text;
        if (string.IsNullOrWhiteSpace(workerUrl))
        {
            workerUri = null;
            error = "set a full https:// worker URL";
            return false;
        }

        if (!Uri.TryCreate(workerUrl.Trim(), UriKind.Absolute, out workerUri))
        {
            workerUri = null;
            error = "enter a valid absolute URL";
            return false;
        }

        if (!string.Equals(workerUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(workerUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            workerUri = null;
            error = "URL must use http or https";
            return false;
        }

        error = null;
        return true;
    }

    private RequestPlan BuildRequestPlan()
    {
        RequestPlan plan = new();
        plan.RequestId = Guid.NewGuid().ToString("N").Substring(0, 12);
        AddPlanHeader(plan, "Accept", "application/json");
        AddPlanHeader(plan, "X-Harness-Transport", GetTransportLabel(GetSelectedTransport()));
        AddPlanHeader(plan, "X-Harness-Signature-Mode", GetSelectedSignatureMode().ToString());
        AddPlanHeader(plan, "X-Harness-Approov", IsApproovEnabled() ? "on" : "off");
        AddPlanHeader(plan, RequestIdHeader, plan.RequestId);

        for (int headerIndex = 0; headerIndex < DefaultRandomHeaderCount; headerIndex++)
        {
            string headerName = GenerateRandomHeaderName();
            string headerValue = GenerateStructuredFieldValue();
            plan.RandomHeaderNames.Add(headerName);
            AddPlanHeader(plan, headerName, headerValue);
        }

        AddPlanHeader(plan, RandomHeadersListHeader, string.Join(", ", plan.RandomHeaderNames));
        plan.Summary = string.Join(" | ", plan.Headers.Select(header => header.Key + "=" + header.Value));
        return plan;
    }

    private static void AddPlanHeader(RequestPlan plan, string name, string value)
    {
        plan.Headers.Add(new System.Collections.Generic.KeyValuePair<string, string>(name, value));
    }

    private static string GenerateRandomHeaderName()
    {
        return "X-Harness-Sfv-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    private static string GenerateStructuredFieldValue()
    {
        switch (UnityEngine.Random.Range(0, 6))
        {
            case 0:
                return UnityEngine.Random.Range(0, 1000000).ToString();
            case 1:
                return UnityEngine.Random.Range(0, 2) == 0 ? "?0" : "?1";
            case 2:
                return "\"" + GenerateRandomAscii(8) + "\"";
            case 3:
                return "token" + GenerateRandomAscii(6).ToLowerInvariant();
            case 4:
                return ":" + Convert.ToBase64String(Encoding.ASCII.GetBytes(GenerateRandomAscii(6))) + ":";
            default:
                int whole = UnityEngine.Random.Range(0, 500);
                int fractional = UnityEngine.Random.Range(0, 999);
                return whole.ToString() + "." + fractional.ToString("000");
        }
    }

    private static string GenerateRandomAscii(int length)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        StringBuilder builder = new(length);
        for (int index = 0; index < length; index++)
        {
            builder.Append(alphabet[UnityEngine.Random.Range(0, alphabet.Length)]);
        }

        return builder.ToString();
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

    private void SelectImage(string imageName)
    {
        string resolvedName = string.IsNullOrWhiteSpace(imageName) ? "confused" : imageName.Trim();
        if (!_imageSprites.TryGetValue(resolvedName, out Sprite sprite))
        {
            sprite = _imageSprites["confused"];
        }

        shapesImage.sprite = sprite;
    }

    private void ConfigureBaseLayout()
    {
        RectTransform imageRect = shapesImage.rectTransform;
        imageRect.anchorMin = new Vector2(0.5f, 1f);
        imageRect.anchorMax = new Vector2(0.5f, 1f);
        imageRect.pivot = new Vector2(0.5f, 1f);
        imageRect.anchoredPosition = new Vector2(0f, -500f);
        imageRect.sizeDelta = new Vector2(220f, 180f);

        RectTransform statusRect = statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 1f);
        statusRect.anchorMax = new Vector2(0.5f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.anchoredPosition = new Vector2(0f, -700f);
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
            throw new InvalidOperationException("Message signing harness could not locate the Canvas.");
        }

        DefaultControls.Resources resources = CreateDefaultControlResources();
        GameObject panelObject = DefaultControls.CreatePanel(resources);
        panelObject.name = "MessageSigningHarnessPanel";
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
        panelRect.sizeDelta = new Vector2(380f, 450f);

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

        _currentConfigText = CreatePanelText(panelObject.transform, 17, FontStyle.Bold, 34f);
        _instructionsText = CreatePanelText(panelObject.transform, 13, FontStyle.Normal, 96f);
        _instructionsText.alignment = TextAnchor.UpperLeft;
        _instructionsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _instructionsText.verticalOverflow = VerticalWrapMode.Overflow;

        _workerUrlInput = CreateInputFieldRow(
            panelObject.transform,
            "Worker URL",
            OnWorkerUrlChanged,
            resources);
        _runCountInput = CreateInputFieldRow(
            panelObject.transform,
            "Run Count",
            OnRunCountChanged,
            resources);

        _approovToggle = CreateToggle(panelObject.transform, "Approov Enabled", OnApproovToggleChanged, resources);
        _transportDropdown = CreateDropdownRow(
            panelObject.transform,
            "Transport",
            new[] { "UnityWebRequest", "HttpClient" },
            OnTransportDropdownChanged,
            resources);
        _signatureDropdown = CreateDropdownRow(
            panelObject.transform,
            "Signature",
            new[] { "None", "Install" },
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
        SetSelectionsWithoutNotify(defaultApproovEnabled, defaultTransport, defaultSignatureMode);
        if (_workerUrlInput != null)
        {
            _workerUrlInput.SetTextWithoutNotify(defaultWorkerUrl);
        }
        if (_runCountInput != null)
        {
            _runCountInput.SetTextWithoutNotify(defaultRunCount.ToString());
        }
    }

    private void SetSelectionsWithoutNotify(
        bool approovEnabled,
        HarnessTransportMode transport,
        HarnessSignatureMode signature)
    {
        _approovToggle.SetIsOnWithoutNotify(approovEnabled);
        _transportDropdown.SetValueWithoutNotify(IndexOf(TransportModes, transport));
        _signatureDropdown.SetValueWithoutNotify(IndexOf(SignatureModes, signature));
        _transportDropdown.RefreshShownValue();
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

        Image backgroundImage = backgroundRect.GetComponent<Image>();
        backgroundRect.sizeDelta = new Vector2(22f, 22f);

        if (backgroundImage != null)
        {
            backgroundImage.color = new Color(0.95f, 0.97f, 1f, 1f);
        }

        Transform defaultCheckmark = backgroundRect.Find("Checkmark");
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

    private InputField CreateInputFieldRow(
        Transform parent,
        string labelText,
        Action<string> onChanged,
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
        rowElement.preferredHeight = 36f;

        Text label = CreatePanelText(rowObject.transform, 14, FontStyle.Normal, 24f);
        label.text = labelText;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleLeft;
        LayoutElement labelElement = label.GetComponent<LayoutElement>();
        labelElement.preferredWidth = 120f;
        labelElement.flexibleWidth = 0f;

        GameObject inputObject = DefaultControls.CreateInputField(resources);
        inputObject.name = labelText.Replace(" ", string.Empty) + "Input";
        inputObject.transform.SetParent(rowObject.transform, false);

        InputField input = inputObject.GetComponent<InputField>();
        input.onEndEdit.RemoveAllListeners();
        input.onEndEdit.AddListener(value => onChanged(value));

        Image backgroundImage = inputObject.GetComponent<Image>();
        if (backgroundImage != null)
        {
            backgroundImage.color = new Color(0.95f, 0.97f, 1f, 1f);
        }

        LayoutElement inputLayout = inputObject.AddComponent<LayoutElement>();
        inputLayout.preferredWidth = 190f;
        inputLayout.flexibleWidth = 1f;

        Text text = input.textComponent;
        if (text != null)
        {
            text.font = statusText.font;
            text.fontSize = 13;
            text.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            text.supportRichText = false;
        }

        if (input.placeholder is Text placeholderText)
        {
            placeholderText.font = statusText.font;
            placeholderText.fontSize = 13;
            placeholderText.color = new Color(0.45f, 0.48f, 0.56f, 0.95f);
            placeholderText.text = DefaultWorkerUrl;
        }

        return input;
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

        StyleDropdownVisuals(dropdownObject);
        return dropdown;
    }

    private void StyleDropdownVisuals(GameObject dropdownObject)
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

    private void OnWorkerUrlChanged(string value)
    {
        LogSample("Worker URL changed to " + (string.IsNullOrWhiteSpace(value) ? "(empty)." : value.Trim() + "."));
        RefreshConfigurationUi();
    }

    private void OnRunCountChanged(string value)
    {
        LogSample("Run count changed to " + GetRunCount() + ".");
        RefreshConfigurationUi();
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

    private void OnSignatureDropdownChanged(int _)
    {
        LogSample("Signature mode changed to " + GetSelectedSignatureMode() + ".");
        ApplyMutatorConfiguration();
        RefreshConfigurationUi();
    }

    private void RefreshConfigurationUi()
    {
        string urlSummary = TryGetWorkerUri(out Uri workerUri, out string workerUrlError)
            ? workerUri.AbsoluteUri
            : "invalid worker URL (" + workerUrlError + ")";

        _instructionsText.text =
            "Focus: install message signatures verified by the Cloudflare worker.\n" +
            "Expected outcomes: Approov Off -> missing token header. " +
            "Approov On + None -> token passes and Signature headers are missing. " +
            "Approov On + Install -> token passes and install signature validates.\n" +
            "Each request adds " + DefaultRandomHeaderCount + " random SFV headers and signs them when install signing is enabled.";

        _currentConfigText.text =
            GetTransportLabel(GetSelectedTransport()) + " | " +
            (IsApproovEnabled() ? "Approov On" : "Approov Off") + " | " +
            GetSignatureSummary() + "\n" +
            "runCount=" + GetRunCount() + " | " + urlSummary;
    }

    private string GetSignatureSummary()
    {
        return GetSelectedSignatureMode() == HarnessSignatureMode.Install
            ? (IsApproovEnabled() ? "Install Signature" : "Install Signature (inactive)")
            : "No Signature";
    }

    private bool IsApproovEnabled()
    {
        return _approovToggle != null && _approovToggle.isOn;
    }

    private HarnessTransportMode GetSelectedTransport()
    {
        return ValueAt(TransportModes, _transportDropdown.value, defaultTransport);
    }

    private HarnessSignatureMode GetSelectedSignatureMode()
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

        if (_signatureDropdown != null)
        {
            _signatureDropdown.interactable = interactable;
        }

        if (_workerUrlInput != null)
        {
            _workerUrlInput.interactable = interactable;
        }
    }

    private void LogOutgoingRequest(int requestId, string prefix, AutoTestScenario scenario, Uri workerUri)
    {
        LogOutgoingRequest(requestId, prefix, scenario, workerUri, null);
    }

    private void LogOutgoingRequest(int requestId, string prefix, AutoTestScenario scenario, Uri workerUri, RequestPlan requestPlan)
    {
        string message =
            "[req-" + requestId + "] " + prefix + " | " +
            scenario.Label + " | " +
            workerUri.AbsoluteUri + " | expected headers=" +
            BuildExpectedHeaderSummary(scenario) + " | config=" +
            BuildConfigurationSnapshot();

        if (requestPlan != null)
        {
            message += " | requestId=" + requestPlan.RequestId +
                       " | randomHeaders=" + string.Join(", ", requestPlan.RandomHeaderNames) +
                       " | headerValues=" + requestPlan.Summary;
        }

        LogSample(message);
    }

    private string BuildExpectedHeaderSummary(AutoTestScenario scenario)
    {
        List<string> headers = new();
        headers.Add(DefaultRandomHeaderCount + " random SFV headers");
        if (scenario.ApproovEnabled)
        {
            headers.Add("Approov-Token");
        }

        if (scenario.ApproovEnabled && scenario.SignatureMode == HarnessSignatureMode.Install)
        {
            headers.Add("Install Signature");
        }

        return headers.Count == 0 ? "none" : string.Join(", ", headers);
    }

    private string BuildConfigurationSnapshot()
    {
        return
            "transport=" + GetSelectedTransport() +
            ", signature=" + GetSelectedSignatureMode() +
            ", approovEnabled=" + IsApproovEnabled() +
            ", sdkInitialized=" + ApproovService.IsSDKInitialized() +
            ", devKeyEnabled=" + IsDevKeyConfigured();
    }

    private void LogRequestResult(int requestId, string prefix, SampleRequestResult result)
    {
        string summary = "[req-" + requestId + "] " + prefix + " => " +
                         (result != null && result.IsSuccess ? "success" : "failure") +
                         " | status=" + result?.StatusCode +
                         " | message=" + GetResultMessage(result);

        if (result != null && result.IsSuccess)
        {
            LogSample(summary);
        }
        else
        {
            LogSampleWarning(summary);
        }

        if (!string.IsNullOrWhiteSpace(result?.Diagnostics))
        {
            Debug.Log("[" + SampleLogTag + "] Diagnostics: " + result.Diagnostics);
        }
    }

    private static string GetResultMessage(SampleRequestResult result)
    {
        if (!string.IsNullOrWhiteSpace(result?.Verification?.reason))
        {
            return result.Verification.reason;
        }

        if (!string.IsNullOrWhiteSpace(result?.Error))
        {
            return result.Error;
        }

        return result?.ResponseBody;
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

    private static string GetTransportLabel(HarnessTransportMode transportMode)
    {
        return transportMode == HarnessTransportMode.HttpClient ? "HttpClient" : "UnityWebRequest";
    }

    private int GetRunCount()
    {
        string rawValue = _runCountInput == null ? defaultRunCount.ToString() : _runCountInput.text;
        if (!int.TryParse(rawValue, out int parsed) || parsed <= 0)
        {
            return Math.Max(1, defaultRunCount);
        }

        return Math.Min(parsed, MaxBatchRunCount);
    }
}
