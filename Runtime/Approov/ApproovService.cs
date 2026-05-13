using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;


namespace Approov
{
    /// <summary>
    /// Service-layer logging verbosity.
    /// </summary>
    public enum ApproovLogLevel
    {
        Off,
        Error,
        Warning,
        Trace
    }

    // DTO used when callers deserialize the JSON returned by GetPinsJSON via JsonUtility.
    [System.Serializable]
    public class KeyValuePair
    {
        public string key;
        public List<string> value;
    }

    /// <summary>
    /// Main entry point for initializing Approov and integrating it with Unity networking surfaces.
    /// </summary>
    public static class ApproovService
    {
        // The config string used to initialize the SDK
        private static string sConfigStringUsed = null;
        /* Lock object: used during ApproovSDK init call */
        private static readonly object InitializerLock = new();
        // The log tag
        public static readonly string TAG = "ApproovService ";
        /* Status of Approov SDK initialisation */
        private static bool ApproovSDKInitialized = false;
        /* Any header to be used for binding in Approov tokens or null if not set */
        private static string BindingHeader = null;
        /* Lock object */
        private static readonly object BindingHeaderLock = new();
        /* Approov token default header */
        private static string ApproovTokenHeader = "Approov-Token";
        /* Approov token custom prefix: any prefix to be added such as "Bearer " */
        private static string ApproovTokenPrefix = "";
        /* Approov TraceID optional header */
        private static string ApproovTraceIDHeader = "Approov-TraceID";
        /* Lock object for the above string variables */
        private static readonly object HeaderAndPrefixLock = new object();
        /* true if the connection should proceed on network failures and not add an Approov token */
        private static bool ProceedOnNetworkFail = false;
        /* Lock object for the above boolean variable*/
        private static readonly object ProceedOnNetworkFailLock = new();
        /* true if the Approov status should be used as the token value when no token is available */
        private static bool UseApproovStatusIfNoToken = false;
        /* Lock object for the above boolean variable */
        private static readonly object UseApproovStatusIfNoTokenLock = new();
        /* map of headers that should have their values substituted for secure strings, mapped to their
            required prefixes */
        private static Dictionary<string, string> SubstitutionHeaders = new Dictionary<string, string>();
        /* Lock object for the above Set*/
        private static readonly object SubstitutionHeadersLock = new();
        /* set of URL regexs that should be excluded from any Approov protection */
        private static HashSet<Regex> ExclusionURLRegexs = new HashSet<Regex>();
        /* Lock object for the above Set*/
        private static readonly object ExclusionURLRegexsLock = new();
        /*  Set of query parameters that may be substituted, specified by the key name */
        private static HashSet<string> SubstitutionQueryParams = new HashSet<string>();
        /* Lock object for the above Set*/
        private static readonly object SubstitutionQueryParamsLock = new();
        /* Service layer logging level */
        private static ApproovLogLevel LoggingLevel = ApproovLogLevel.Warning;
        /* Lock object for the logging level */
        private static readonly object LoggingLevelLock = new();
        /* Service mutator used to customize request and fetch handling */
        private static ApproovServiceMutator ServiceMutator = ApproovServiceMutator.Default;
        private static readonly object ServiceMutatorLock = new();
        /* Native SDK state that affects subsequent fetches, such as token binding, is process-wide. */
        private static readonly object NativeStateLock = new();
        private static string DataHashInToken = null;


        /*  
        *   Initializes the Approov SDK with provided config string
        *   Can throw an initialization failure exception
        *   @param config string with the configuration
        *
        */
        public static void Initialize()
        {
            Initialize(ApproovProjectConfig.GetConfigString());
        }

        /// <summary>
        /// Initializes the native Approov SDK with an explicit config string.
        /// </summary>
        public static void Initialize(string config){
            LogTrace(TAG + "Initialize requested for platform " + Application.platform);
            if (string.IsNullOrWhiteSpace(config))
            {
                throw new ConfigurationFailureException(TAG + "Approov config string is missing. Open Tools/Approov/Approov Settings and paste the output of `approov sdk -getConfigString`.");
            }

            lock (InitializerLock)
            {
                // Check if attempting to use a different config string
                if (ApproovSDKInitialized)
                {
                    // Check if attempting to use a different config string
                        if ((sConfigStringUsed != null) && (sConfigStringUsed != config))
                        {
                            throw new ConfigurationFailureException(TAG + "Error: SDK already initialized");
                        }

                    return;
                }

                if (!IsNativeInitializationSupported())
                {
                    LogTrace(TAG + "Initialize skipped because native initialization is not supported on this platform");
                    LogWarning(TAG + "Approov native initialization is only available on iOS and Android. The config string is stored in project settings, but Approov will remain disabled in this editor or desktop session.");
                    return;
                }

                LogTrace(TAG + "Starting native SDK initialization");
                // Initialize the SDK
#if UNITY_ANDROID
                ApproovBridge.Initialize(config);
#elif UNITY_IOS
                // iOS
                bool statusInit = ApproovBridge.Initialize(config, "auto", null, out _);
                if (!statusInit)
                {
                    throw new InitializationFailureException(TAG + "Error SDK initialization failed", false);
                }
#else
                LogWarning(TAG + "Approov native initialization is not available on this platform.");
                return;
#endif
                sConfigStringUsed = config;
                ApproovSDKInitialized = true;

                // Set a default user property so Approov logs can identify the service layer and app.
                try
                {
                    string defaultUserProperty = BuildDefaultUserProperty();
                    SetUserProperty(defaultUserProperty);

                    Debug.Log(TAG + "Approov successfully initialized");
                    Debug.Log(TAG + "Approov user property: " + defaultUserProperty);
                    string deviceID = GetDeviceID();
                    if (string.IsNullOrWhiteSpace(deviceID))
                    {
                        LogWarning(TAG + "Unable to read the Approov device ID after initialization.");
                    }
                    else
                    {
                        Debug.Log(TAG + "Approov device ID: " + deviceID);
                    }
                }
                catch (Exception ex)
                {
                    LogWarning(TAG + "Approov initialized but post-initialization metadata setup failed: " + ex.Message);
                }
            }  
        }// Initialize method

        private static bool IsNativeInitializationSupported()
        {
#if UNITY_ANDROID || UNITY_IOS
            return Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;
#else
            return false;
#endif
        }
        
        /*  Get initialization status of SDK
        *   @param true if the SDK is initialized
        */
        public static bool IsSDKInitialized()
        {
            lock (InitializerLock)
            {
                return ApproovSDKInitialized;
            }
        }

        /// <summary>
        /// Creates an <see cref="HttpClient"/> that routes requests through <see cref="ApproovHttpClientHandler"/>.
        /// </summary>
        public static HttpClient CreateHttpClient()
        {
            return new HttpClient(new ApproovHttpClientHandler(), disposeHandler: true);
        }

        /// <summary>
        /// Creates an <see cref="HttpClient"/> that wraps an existing handler chain with Approov processing.
        /// </summary>
        public static HttpClient CreateHttpClient(HttpMessageHandler innerHandler)
        {
            return new HttpClient(new ApproovHttpClientHandler(innerHandler), disposeHandler: true);
        }

        /// <summary>
        /// Creates a standalone <see cref="ApproovHttpClientHandler"/>.
        /// </summary>
        public static ApproovHttpClientHandler CreateHttpClientHandler()
        {
            return new ApproovHttpClientHandler();
        }

        /// <summary>
        /// Creates a standalone <see cref="ApproovHttpClientHandler"/> around an existing inner handler.
        /// </summary>
        public static ApproovHttpClientHandler CreateHttpClientHandler(HttpMessageHandler innerHandler)
        {
            return new ApproovHttpClientHandler(innerHandler);
        }

        /// <summary>
        /// Applies Approov protection to a UnityWebRequest without blocking the Unity main thread
        /// while the native SDK fetches tokens or secure-string substitutions.
        /// </summary>
        public static System.Collections.IEnumerator SendWebRequest(UnityWebRequest request)
        {
            PrepareUnityWebRequest(request, "SendWebRequest");
            LogTrace(TAG + "SendWebRequest applying Approov processing to " + request.url);
            yield return ApproovRequestProcessor.ApplyToUnityWebRequestAsync(request);
            ApplyUnityWebRequestPinning(request, "SendWebRequest");
            yield return request.SendWebRequest();
        }

        private static void PrepareUnityWebRequest(UnityWebRequest request, string operation)
        {
            EnsureSDKInitialized(operation);

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.downloadHandler == null)
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                LogTrace(TAG + operation + " attached DownloadHandlerBuffer");
            }
        }

        private static void ApplyUnityWebRequestPinning(UnityWebRequest request, string operation)
        {
            ApproovRequestContext pinningContext = ApproovRequestContext.CreateSnapshot(request);
            if (ShouldApplyPinning(pinningContext))
            {
                CertificateHandler oldHandler = request.certificateHandler;
                if (oldHandler != null && !(oldHandler is ApproovCertificateHandler))
                {
                    LogWarning(TAG + operation + " replaced existing CertificateHandler with ApproovCertificateHandler so Approov pinning cannot be bypassed");
                }

                request.certificateHandler = new ApproovCertificateHandler(request);
                oldHandler?.Dispose();
                LogTrace(TAG + operation + " refreshed ApproovCertificateHandler");
            }
            else if (request.certificateHandler is ApproovCertificateHandler)
            {
                CertificateHandler oldHandler = request.certificateHandler;
                request.certificateHandler = null;
                oldHandler.Dispose();
                LogTrace(TAG + operation + " removed ApproovCertificateHandler because pinning was skipped by mutator");
            }
        }

        /**
        * Sets the service-layer logging level. TRACE enables detailed step-by-step diagnostics.
        */
        public static void SetLoggingLevel(ApproovLogLevel level)
        {
            lock (LoggingLevelLock)
            {
                LoggingLevel = level;
            }

            Debug.Log(TAG + "SetLoggingLevel " + level);
        }

        /**
        * Gets the current service-layer logging level.
        */
        public static ApproovLogLevel GetLoggingLevel()
        {
            lock (LoggingLevelLock)
            {
                return LoggingLevel;
            }
        }

        /**
        * Convenience API to toggle detailed debug logging.
        */
        public static void SetDetailedDebugLogging(bool enabled)
        {
            SetLoggingLevel(enabled ? ApproovLogLevel.Trace : ApproovLogLevel.Warning);
        }

        /**
        * Gets whether detailed debug logging is enabled.
        */
        public static bool GetDetailedDebugLogging()
        {
            return GetLoggingLevel() == ApproovLogLevel.Trace;
        }

        internal static bool ShouldLog(ApproovLogLevel level)
        {
            return level != ApproovLogLevel.Off && GetLoggingLevel() >= level;
        }

        internal static void LogError(string message)
        {
            if (ShouldLog(ApproovLogLevel.Error))
            {
                Debug.LogError(message);
            }
        }

        internal static void LogWarning(string message)
        {
            if (ShouldLog(ApproovLogLevel.Warning))
            {
                Debug.LogWarning(message);
            }
        }

        internal static void LogTrace(string message)
        {
            if (ShouldLog(ApproovLogLevel.Trace))
            {
                Debug.Log(message);
            }
        }

        /**
        * Sets the header that should be used for binding in Approov tokens. This is the header that
        * will be used to bind the Approov token to the request. If this is not set then no binding
        * will be performed. Note that the binding header must be set before the Approov SDK is
        * initialized.
        *
        * @param header is the header to be used for binding in Approov tokens
        */
        public static void SetBindingHeader(string header)
        {
            lock (BindingHeaderLock)
            {
                BindingHeader = header;
                LogTrace(TAG + "SetBindingHeader " + header);
            }
        }

        /**
        * Gets the header that should be used for binding in Approov tokens.
        */
        public static string GetBindingHeader()
        {
            lock (BindingHeaderLock)
            {
                return BindingHeader;
            }
        }

        /*  Sets the Approov Header and optional prefix. By default, those values are "Approov-Token"
        *  for the header and the prefix is an empty string. If you wish to use "Authorization Bearer .."
        *  for example, the header should be set to "Authorization " and the prefix to "Bearer"
        *  
        *  @param  header the header to use
        *  @param  prefix optional prefix, can be an empty string if not needed
        */
        public static void SetTokenHeaderAndPrefix(string header, string prefix)
        {
            lock (HeaderAndPrefixLock)
            {
                if (header != null) ApproovTokenHeader = header;
                if (prefix != null) ApproovTokenPrefix = prefix;
                LogTrace(TAG + "SetTokenHeaderAndPrefix header: " + header + " prefix: " + prefix);
            }
        }

        /*  Getter for token header */
        public static string GetTokenHeader()
        {
            lock (HeaderAndPrefixLock)
            {
                return ApproovTokenHeader;
            }
        }
        /* Getter for token prefix */
        public static string GetTokenPrefix()
        {
            lock (HeaderAndPrefixLock)
            {
                return ApproovTokenPrefix;
            }
        }

        /*
        * Sets the header that receives the optional Approov TraceID value. Passing null disables it.
        *
        * @param header the header to use for the Approov TraceID, or null to disable
        */
        public static void SetApproovTraceIDHeader(string header)
        {
            lock (HeaderAndPrefixLock)
            {
                ApproovTraceIDHeader = header;
                LogTrace(TAG + "SetApproovTraceIDHeader " + (header ?? "null"));
            }
        }

        /* Getter for the optional Approov TraceID header */
        public static string GetApproovTraceIDHeader()
        {
            lock (HeaderAndPrefixLock)
            {
                return ApproovTraceIDHeader;
            }
        }
        
        /*
        * Sets a flag indicating if the network interceptor should proceed anyway if it is
        * not possible to obtain an Approov token due to a networking failure. If this is set
        * then your backend API can receive calls without the expected Approov token header
        * being added, or without header/query parameter substitutions being made. Note that
        * this should be used with caution because it may allow a connection to be established
        * before any dynamic pins have been received via Approov, thus potentially opening the channel to a MitM.
        *
        * @param proceed is true if Approov networking fails should allow continuation
        */
        public static void SetProceedOnNetworkFailure(bool proceed)
        {
            lock (ProceedOnNetworkFailLock)
            {
                ProceedOnNetworkFail = proceed;
                LogTrace(TAG + "SetProceedOnNetworkFailure " + proceed);
            }
        }

        /*
        * Gets a flag indicating if the network interceptor should proceed anyway if it is
        * not possible to obtain an Approov token due to a networking failure. If this is set
        * then your backend API can receive calls without the expected Approov token header
        * being added, or without header/query parameter substitutions being made. Note that
        * this should be used with caution because it may allow a connection to be established
        * before any dynamic pins have been received via Approov, thus potentially opening the channel to a MitM.
        *
        * @return boolean true if Approov networking fails should allow continuation
        */
        public static bool GetProceedOnNetworkFailure()
        {
            lock (ProceedOnNetworkFailLock)
            {
                return ProceedOnNetworkFail;
            }
        }

        /// <summary>
        /// Controls whether the textual Approov fetch status should be used as the token value
        /// when a request is allowed to proceed without a fetched token.
        /// </summary>
        public static void SetUseApproovStatusIfNoToken(bool shouldUse)
        {
            lock (UseApproovStatusIfNoTokenLock)
            {
                UseApproovStatusIfNoToken = shouldUse;
                LogTrace(TAG + "SetUseApproovStatusIfNoToken " + shouldUse);
            }
        }

        /// <summary>
        /// Returns whether the service layer should substitute the textual fetch status when no token is available.
        /// </summary>
        public static bool GetUseApproovStatusIfNoToken()
        {
            lock (UseApproovStatusIfNoTokenLock)
            {
                return UseApproovStatusIfNoToken;
            }
        }

        /// <summary>
        /// Installs a mutator that can customize fetch-result handling, request processing, and pinning.
        /// </summary>
        public static void SetServiceMutator(ApproovServiceMutator mutator)
        {
            lock (ServiceMutatorLock)
            {
                ServiceMutator = mutator ?? ApproovServiceMutator.Default;
                LogTrace(TAG + "SetServiceMutator " + ServiceMutator);
            }
        }

        /// <summary>
        /// Returns the currently active service mutator, never <c>null</c>.
        /// </summary>
        public static ApproovServiceMutator GetServiceMutator()
        {
            lock (ServiceMutatorLock)
            {
                return ServiceMutator ?? ApproovServiceMutator.Default;
            }
        }

        /*
        * Adds the name of a header which should be subject to secure strings substitution. This
        * means that if the header is present then the value will be used as a key to look up a
        * secure string value which will be substituted into the header value instead. This allows
        * easy migration to the use of secure strings. A required prefix may be specified to deal
        * with cases such as the use of "Bearer " prefixed before values in an authorization header.
        *
        * @param header is the header to be marked for substitution
        * @param requiredPrefix is any required prefix to the value being substituted or nil if not required
        */
        public static void AddSubstitutionHeader(string header, string requiredPrefix)
        {
            lock (SubstitutionHeadersLock)
            {
                SubstitutionHeaders[header] = requiredPrefix ?? string.Empty;
                LogTrace(TAG + "AddSubstitutionHeader header: " + header + " requiredPrefix: " + requiredPrefix);
            }
        }

        /*
        * Removes a header previously added using addSubstitutionHeader.
        *
        * @param header is the header to be removed for substitution
        */
        public static void RemoveSubstitutionHeader(string header)
        {
            lock (SubstitutionHeadersLock)
            {
                if (SubstitutionHeaders.ContainsKey(header))
                {
                    SubstitutionHeaders.Remove(header);
                    LogTrace(TAG + "RemoveSubstitutionHeader " + header);
                }
            }
        }

        /* Get a copy of the SubstitutionHeaders object. NOTE that you get a copy
        *  and not the actual object since it can be modified by other threads whilst
        *  you are using it!!!!!
        */
        public static Dictionary<string, string> GetSubstitutionHeaders()
        {
            lock (SubstitutionHeadersLock)
            {
                return new Dictionary<string, string>(SubstitutionHeaders);
            }
        }

        /**
        * Adds an exclusion URL regular expression. If a URL for a request matches this regular expression
        * then it will not be subject to any Approov protection. Note that this facility must be used with
        * EXTREME CAUTION due to the impact of dynamic pinning. Pinning may be applied to all domains added
        * using Approov, and updates to the pins are received when an Approov fetch is performed. If you
        * exclude some URLs on domains that are protected with Approov, then these will be protected with
        * Approov pins but without a path to update the pins until a URL is used that is not excluded. Thus
        * you are responsible for ensuring that there is always a possibility of calling a non-excluded
        * URL, or you should make an explicit call to fetchToken if there are persistent pinning failures.
        * Conversely, use of those option may allow a connection to be established before any dynamic pins
        * have been received via Approov, thus potentially opening the channel to a MitM.
        *
        * @param urlRegex is the regular expression that will be compared against URLs to exclude them
        * @throws ArgumentException if urlRegex is malformed
        */
        public static void AddExclusionURLRegex(string urlRegex)
        {
            lock (ExclusionURLRegexsLock)
            {
                if (urlRegex != null)
                {
                    Regex reg = new Regex(urlRegex);
                    foreach (Regex existing in ExclusionURLRegexs)
                    {
                        if (existing.ToString() == urlRegex)
                        {
                            return;
                        }
                    }
                    ExclusionURLRegexs.Add(reg);
                    LogTrace(TAG + "AddExclusionURLRegex " + urlRegex);
                }
            }
        }

        /**
        * Removes an exclusion URL regular expression previously added using addExclusionURLRegex.
        * @param urlRegex is the regular expression that will be compared against URLs to exclude them
        * @throws ArgumentException if urlRegex is malformed
        */
        public static void RemoveExclusionURLRegex(string urlRegex)
        {
            lock (ExclusionURLRegexsLock)
            {
                if (urlRegex != null)
                {
                    _ = new Regex(urlRegex);
                    Regex regexToRemove = null;
                    foreach (Regex existing in ExclusionURLRegexs)
                    {
                        if (existing.ToString() == urlRegex)
                        {
                            regexToRemove = existing;
                            break;
                        }
                    }
                    if (regexToRemove != null)
                    {
                        ExclusionURLRegexs.Remove(regexToRemove);
                    }
                    LogTrace(TAG + "RemoveExclusionURLRegex " + urlRegex);
                }
            }
        }

        /**
        * Checks if the url matches one of the exclusion regexs defined in exclusionURLRegexs
        * @param   url is the URL for which the check is performed
        * @return  Bool true if url matches preset pattern in Dictionary
        */
        public static bool CheckURLIsExcluded(string url)
        {
            // obtain a copy of the exclusion URL regular expressions in a thread safe way
            int elementCount;
            Regex[] exclusionURLs;
            lock (ExclusionURLRegexsLock)
            {
                elementCount = ExclusionURLRegexs.Count;
                if (elementCount == 0) return false;
                exclusionURLs = new Regex[elementCount];
                ExclusionURLRegexs.CopyTo(exclusionURLs);
            }

            foreach (Regex pattern in exclusionURLs)
            {
                Match match = pattern.Match(url, 0, url.Length);
                if (match.Length > 0)
                {
                    LogTrace(TAG + "CheckURLIsExcluded match for " + url);
                    return true;
                }
            }
            return false;
        }

        /**
            * Adds a key name for a query parameter that should be subject to secure strings substitution.
            * This means that if the query parameter is present in a URL then the value will be used as a
            * key to look up a secure string value which will be substituted as the query parameter value
            * instead. This allows easy migration to the use of secure strings.
            *
            * @param key is the query parameter key name to be added for substitution
            */
        public static void AddSubstitutionQueryParam(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (SubstitutionQueryParamsLock)
            {
                SubstitutionQueryParams.Add(key);
                LogTrace(TAG + "AddSubstitutionQueryParam " + key);
            }
        }

        /**
        * Removes a query parameter key name previously added using addSubstitutionQueryParam.
        * @param key is the query parameter key name to be removed for substitution
        */
        public static void RemoveSubstitutionQueryParam(string key)
        {
            lock (SubstitutionQueryParamsLock)
            {
                if (SubstitutionQueryParams.Contains(key))
                {
                    SubstitutionQueryParams.Remove(key);
                    LogTrace(TAG + "RemoveSubstitutionQueryParam " + key);
                }
            }
        }

        /*  Get a copy of the SubstitutionQueryParams object. NOTE that you get a copy
        *   and not the actual object since it can be modified by other threads whilst
        *   you are using it!!!!!
        */
        public static HashSet<string> GetSubstitutionQueryParams()
        {
            lock (SubstitutionQueryParamsLock)
            {
                return new HashSet<string>(SubstitutionQueryParams);
            }
        }


        /**
        * Sets a user defined property on the SDK. This may provide information about the
        * app state or aspects of the environment it is running in. This has no direct
        * impact on Approov except it is visible as a property on attesting devices and
        * can be analyzed using device filters. Note that properties longer than 128
        * characters are ignored and all non ASCII characters are removed. The special
        * value "$error" may be used to mark an error condition for offline measurement
        * mismatches.
        *
        * @param property to be set, which may be null
        */
        public static void SetUserProperty(string property)  {
            LogTrace(TAG + "SetUserProperty " + property);
            ApproovBridge.SetUserProperty(property);
        }

        private static string BuildDefaultUserProperty()
        {
            string packageVersion = ApproovProjectConfig.GetPackageVersion();
            string appName = Application.productName;
            if (string.IsNullOrWhiteSpace(appName))
            {
                appName = Application.identifier;
            }

            if (string.IsNullOrWhiteSpace(appName))
            {
                appName = "unknown-app";
            }

            string prefix = "approov-service-unity:" + packageVersion + ", ";
            int remainingLength = 128 - prefix.Length;
            if (remainingLength <= 0)
            {
                return prefix.Substring(0, 128);
            }

            if (appName.Length > remainingLength)
            {
                appName = appName.Substring(0, remainingLength);
            }

            return prefix + appName;
        }

        #if UNITY_ANDROID
        /**
        * Sets the information about a current activity. This may be set for an expected app
        * launch activity so that analysis can be performed to determine if the activity may have
        * been launched in an automatic way. A flag indicating this can then be included as an
        * annotation in the Approov token.
        *
        * @param activity is the current activity that is being run which must be obtained using: 
        *   AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        *   AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        *   ApproovService.SetActivity(currentActivity);
        */

        public static void SetActivity(AndroidJavaObject activity) {
            ApproovBridge.SetActivity(activity);
        }
        #endif
        /*
        *  Allows SDK configuration prefetch to run as early as possible. Use the URL
        *  overload when the app can identify the protected API domain it wants to warm.
        */
        public static void Prefetch() {
            if (!IsSDKInitialized())
            {
                return;
            }

            LogTrace(TAG + "Prefetch requested for SDK configuration");
            _ = HandlePrefetchAsync(FetchConfig, "SDK configuration");
        }

        /// <summary>
        /// Performs an early background token fetch for the supplied protected URL.
        /// </summary>
        public static void Prefetch(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (!IsSDKInitialized())
            {
                return;
            }

            LogTrace(TAG + "Prefetch requested for " + url);
            _ = HandlePrefetchAsync(() => FetchToken(url), url);
        }

        private static async Task HandlePrefetchAsync(Func<string> prefetchOperation, string description)
        {
            try
            {
                _ = await Task.Run(prefetchOperation).ConfigureAwait(false);
                LogTrace(TAG + "Prefetch completed for " + description);
            }
            catch (Exception exception)
            {
                LogWarning(TAG + "Prefetch failed for " + description + ": " + exception.Message);
            }
        }

        internal static T ExecuteWithNativeState<T>(Func<T> operation)
        {
            lock (NativeStateLock)
            {
                return operation();
            }
        }

        internal static void ExecuteWithNativeState(Action operation)
        {
            lock (NativeStateLock)
            {
                operation();
            }
        }

        internal static ApproovTokenFetchResult FetchApproovTokenWithNativeState(string url)
        {
            return FetchApproovTokenWithNativeState(url, null, false);
        }

        internal static ApproovTokenFetchResult FetchApproovTokenWithNativeState(string url, string bindingValue)
        {
            return FetchApproovTokenWithNativeState(url, bindingValue, true);
        }

        private static ApproovTokenFetchResult FetchApproovTokenWithNativeState(string url, string bindingValue, bool restoreExplicitDataHash)
        {
            lock (NativeStateLock)
            {
                string explicitDataHash = DataHashInToken;
                ApproovBridge.SetDataHashInToken(restoreExplicitDataHash ? bindingValue : explicitDataHash);
                try
                {
                    ApproovTokenFetchResult fetchResult = ApproovBridge.FetchApproovTokenAndWait(url);
                    HandleTokenFetchSideEffects(fetchResult, "Approov token fetch for " + url);
                    return fetchResult;
                }
                finally
                {
                    if (restoreExplicitDataHash)
                    {
                        ApproovBridge.SetDataHashInToken(explicitDataHash);
                    }
                }
            }
        }

        internal static void HandleTokenFetchSideEffects(ApproovTokenFetchResult fetchResult, string operation)
        {
            if (fetchResult.isConfigChanged)
            {
                // A token fetch can also deliver refreshed dynamic pinning state. Clear the transport-side
                // certificate cache and mark the SDK config as consumed so later fetches see a clean state.
                LogTrace(TAG + operation + " SDK configuration changed, refreshing pin state");
                ApproovBridge.ClearCertificateCache();
                FetchConfig();
            }

            if (fetchResult.isForceApplyPins)
            {
                throw new NetworkingErrorException(TAG + operation + ": forced pin update required", true);
            }
        }

        private static void EnsureSDKInitialized(string operation)
        {
            if (!IsSDKInitialized())
            {
                throw new InitializationFailureException(TAG + operation + ": SDK not initialized");
            }
        }

        // MARK: Approov SDK methods
        // Auxiliary method to print FetchStatus from Approov SDK
        public static string ApproovTokenFetchStatusToString(ApproovTokenFetchStatus status)
        {
            switch (status)
            {
                case ApproovTokenFetchStatus.Success:
                    return "SUCCESS";
                case ApproovTokenFetchStatus.NoNetwork:
                    return "NO_NETWORK";
                case ApproovTokenFetchStatus.MITMDetected:
                    return "MITM_DETECTED";
                case ApproovTokenFetchStatus.PoorNetwork:
                    return "POOR_NETWORK";
                case ApproovTokenFetchStatus.NoApproovService:
                    return "NO_APPROOV_SERVICE";
                case ApproovTokenFetchStatus.BadURL:
                    return "BAD_URL";
                case ApproovTokenFetchStatus.UnknownURL:
                    return "UNKNOWN_URL";
                case ApproovTokenFetchStatus.UnprotectedURL:
                    return "UNPROTECTED_URL";
                case ApproovTokenFetchStatus.NotInitialized:
                    return "NOT_INITIALIZED";
                case ApproovTokenFetchStatus.NoNetworkPermission:
                    return "NO_NETWORK_PERMISSION";
                case ApproovTokenFetchStatus.MissingLibDependency:
                    return "MISSING_LIB_DEPENDENCY";
                case ApproovTokenFetchStatus.InternalError:
                    return "INTERNAL_ERROR";
                case ApproovTokenFetchStatus.Rejected:
                    return "REJECTED";
                case ApproovTokenFetchStatus.Disabled:
                    return "DISABLED";
                case ApproovTokenFetchStatus.UnknownKey:
                    return "UNKNOWN_KEY";
                case ApproovTokenFetchStatus.BadKey:
                    return "BAD_KEY";
                case ApproovTokenFetchStatus.BadPayload:
                    return "BAD_PAYLOAD";
                default:
                    return "UNKNOWN";
            }
        }

        public static string DescribeFetchResult(ApproovTokenFetchResult fetchResult)
        {
            List<string> parts = new()
            {
                "status=" + ApproovTokenFetchStatusToString(fetchResult.status),
                "configChanged=" + fetchResult.isConfigChanged,
                "forceApplyPins=" + fetchResult.isForceApplyPins,
                "hasToken=" + !string.IsNullOrWhiteSpace(fetchResult.token),
                "hasTraceID=" + !string.IsNullOrWhiteSpace(fetchResult.traceID),
                "hasSecureString=" + !string.IsNullOrWhiteSpace(fetchResult.secureString),
            };

            if (!string.IsNullOrWhiteSpace(fetchResult.token))
            {
                parts.Add("tokenLength=" + fetchResult.token.Length);
            }

            if (!string.IsNullOrWhiteSpace(fetchResult.traceID))
            {
                parts.Add("traceID=" + fetchResult.traceID);
            }

            if (!string.IsNullOrWhiteSpace(fetchResult.ARC))
            {
                parts.Add("ARC=" + fetchResult.ARC);
            }

            if (!string.IsNullOrWhiteSpace(fetchResult.rejectionReasons))
            {
                parts.Add("rejectionReasons=" + fetchResult.rejectionReasons);
            }

            if (!string.IsNullOrWhiteSpace(fetchResult.loggableToken))
            {
                parts.Add("loggableToken=" + fetchResult.loggableToken);
            }

            return string.Join(" | ", parts);
        }

        /*
        * Fetches a secure string with the given key. If newDef is not nil then a secure string for
        * the particular app instance may be defined. In this case the new value is returned as the
        * secure string. Use of an empty string for newDef removes the string entry. Note that this
        * call may require network transaction and thus may block for some time, so should not be called
        * from the UI thread. If the attestation fails for any reason then an exception is raised. Note
        * that the returned string should NEVER be cached by your app, you should call this function when
        * it is needed.
        *
        * @param key is the secure string key to be looked up
        * @param newDef is any new definition for the secure string, or nil for lookup only
        * @return secure string (should not be cached by your app) or nil if it was not defined or an error ocurred
        * @throws exception with description of cause
        */
        public static string FetchSecureString(string key, string newDef)
        {
            EnsureSDKInitialized("FetchSecureString");
            string type = "lookup";
            if (newDef != null)
            {
                type = "definition";
            }
            LogTrace(TAG + "FetchSecureString start type=" + type + " key=" + key);

            ApproovTokenFetchResult fetchResult = ExecuteWithNativeState(() => ApproovBridge.FetchSecureStringAndWait(key, newDef));
            LogTrace(TAG + "FetchSecureString: " + type + " " + ApproovTokenFetchStatusToString(fetchResult.status));
            GetServiceMutator().HandleFetchSecureStringResult(fetchResult, type, key);
            return fetchResult.secureString;
        }//FetchSecureString

        /*
        * Fetches a custom JWT with the given payload. Note that this call will require network
        * transaction and thus will block for some time, so should not be called from the UI thread.
        * If the fetch fails for any reason an exception will be thrown. 
        *
        * @param payload is the marshaled JSON object for the claims to be included
        * @return custom JWT string or nil if an error occurred
        * @throws exception with description of cause
        */
        public static string FetchCustomJWT(string payload)
        {
            EnsureSDKInitialized("FetchCustomJWT");
            LogTrace(TAG + "FetchCustomJWT start payloadLength=" + (payload?.Length ?? 0));
            ApproovTokenFetchResult fetchResult;
            ApproovTokenFetchStatus aCurrentFetchStatus = ApproovTokenFetchStatus.NoApproovService  ;
            try {
            fetchResult = ExecuteWithNativeState(() => ApproovBridge.FetchCustomJWTAndWait(payload));
            aCurrentFetchStatus = fetchResult.status;
            } catch (Exception e) {
                LogWarning(TAG + "FetchCustomJWT: " + e.Message);
                throw new PermanentException(TAG + "FetchCustomJWT: " + e.Message);
            }
            LogTrace(TAG + "FetchCustomJWT: " + ApproovTokenFetchStatusToString((ApproovTokenFetchStatus)aCurrentFetchStatus));

            GetServiceMutator().HandleFetchCustomJwtResult(fetchResult);
            return fetchResult.token;
        }// FetchCustomJWT

        /*
        * Performs a precheck to determine if the app will pass attestation. This requires secure
        * strings to be enabled for the account, although no strings need to be set up. This will
        * likely require network access so may take some time to complete. It may throw an exception
        * if the precheck fails or if there is some other problem. 
        */
        public static void Precheck()
        {
            EnsureSDKInitialized("Precheck");
            LogTrace(TAG + "Precheck start");
            
            ApproovTokenFetchResult fetchResult = ExecuteWithNativeState(() => ApproovBridge.FetchSecureStringAndWait("precheck-dummy-key", null));
            GetServiceMutator().HandlePrecheckResult(fetchResult);
            // Get loggable token and print
            string loggableToken = fetchResult.loggableToken;
            
            LogTrace(TAG + "Precheck " + loggableToken);
        }// Precheck

        /**
        * Gets the device ID used by Approov to identify the particular device that the SDK is running on. Note
        * that different Approov apps on the same device will return a different ID. Moreover, the ID may be
        * changed by an uninstall and reinstall of the app.
        *
        * @return String of the device ID or null in case of an error
        */
        public static string GetDeviceID()
        {
            EnsureSDKInitialized("GetDeviceID");
            LogTrace(TAG + "GetDeviceID start");
            string deviceID = ApproovBridge.GetDeviceID();
            LogTrace(TAG + "DeviceID: " + deviceID);
            return deviceID;
        }

        /**
        * Directly sets the data hash to be included in subsequently fetched Approov tokens. If the hash is
        * different from any previously set value then this will cause the next token fetch operation to
        * fetch a new token with the correct payload data hash. The hash appears in the
        * 'pay' claim of the Approov token as a base64 encoded string of the SHA256 hash of the
        * data. Note that the data is hashed locally and never sent to the Approov cloud service.
        *
        * @param data is the data to be hashed and set in the token
        */
        public static void SetDataHashInToken(string data)
        {
            LogTrace(TAG + "SetDataHashInToken valueLength=" + (data?.Length ?? 0));
            LogTrace(TAG + "SetDataHashInToken");
            lock (NativeStateLock)
            {
                DataHashInToken = data;
                ApproovBridge.SetDataHashInToken(data);
            }
        }

        /**
        * Gets the signature for the given message. This uses an account specific message signing key that is
        * transmitted to the SDK after a successful fetch if the facility is enabled for the account. Note
        * that if the attestation failed then the signing key provided is actually random so that the
        * signature will be incorrect. An Approov token should always be included in the message
        * being signed and sent alongside this signature to prevent replay attacks. If no signature is
        * available, because there has been no prior fetch or the feature is not enabled, then an
        * ApproovException is thrown.
        *
        * @param message is the message whose content is to be signed
        * @return String of the base64 encoded message signature
        */
        [Obsolete("Use GetAccountMessageSignature or GetInstallMessageSignature instead.")]
        public static string GetMessageSignature(string message)
        {
            return GetAccountMessageSignature(message);
        }

        /// <summary>
        /// Returns a base64-encoded account-key signature for the exact message string supplied.
        /// </summary>
        public static string GetAccountMessageSignature(string message)
        {
            EnsureSDKInitialized("GetAccountMessageSignature");
            LogTrace(TAG + "GetAccountMessageSignature start messageLength=" + (message?.Length ?? 0));
            string signature = ApproovBridge.GetAccountMessageSignature(message);
            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new ApproovException(TAG + "GetAccountMessageSignature: no account signature available");
            }

            return signature;
        }

        /// <summary>
        /// Returns a base64-encoded install-key signature for the exact message string supplied.
        /// </summary>
        public static string GetInstallMessageSignature(string message)
        {
            EnsureSDKInitialized("GetInstallMessageSignature");
            LogTrace(TAG + "GetInstallMessageSignature start messageLength=" + (message?.Length ?? 0));
            string signature = ApproovBridge.GetInstallMessageSignature(message);
            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new ApproovException(TAG + "GetInstallMessageSignature: no install signature available");
            }

            return signature;
        }

        /**
        * Performs an Approov token fetch for the given URL. This should be used in situations where it
        * is not possible to use the networking interception to add the token. This will
        * likely require network access so may take some time to complete. If the attestation fails
        * for any reason then an Exception is thrown. ... Note that
        * the returned token should NEVER be cached by your app, you should call this function when
        * it is needed.
        *
        * @param url is the URL giving the domain for the token fetch
        * @return string    jwt token from token fetch
        * @throws Exception if there was a problem
        */

        public static string FetchToken(string url)
        {
            EnsureSDKInitialized("FetchToken");
            LogTrace(TAG + "FetchToken start url=" + url);
            // Invoke fetchApproovTokenAndWait
            ApproovTokenFetchResult fetchResult = FetchApproovTokenWithNativeState(url);
            ApproovTokenFetchStatus aCurrentFetchStatus = fetchResult.status;

            // Process the result
            LogTrace(TAG + "FetchToken: " + url + " " + ApproovTokenFetchStatusToString(aCurrentFetchStatus));
            GetServiceMutator().HandleFetchTokenResult(fetchResult);
            return fetchResult.token;
        }// FetchToken

        /// <summary>
        /// Returns the currently cached Approov pins as JSON for the requested pin type.
        /// </summary>
        public static string GetPinsJSON(string pinType)
        {   
            string approovPinsJNI = ApproovBridge.GetPinsJSON(pinType);
            return approovPinsJNI;
        }

        /**
        * Fetches the current configuration for the SDK. This may be the initial configuration or may
        * be a new updated configuration returned from the Approov cloud service. Such updates of the
        * configuration allow new sets of certificate pins and other configuration to be passed to
        * an app instance that is running in the field.
        *
        * Normally this method returns the latest configuration that is available and is cached in the
        * SDK. Thus the method will return quickly. However, if this method is called when there has
        * been no prior call to fetch an Approov token then a network request to the Approov cloud
        * service will be made to obtain any latest configuration update. The maximum timeout period
        * is set to be quite short but the caller must be aware that this delay may occur.
        *
        * Note that the returned configuration should generally be kept in local storage for the app
        * so that it can be made available on initialization of the Approov SDK next time the app
        * is started.
        *
        * It is possible to see if a new configuration becomes available from the isConfigChanged()
        * method of the TokenFetchResult. This changed flag is only cleared for future token fetches
        * if a call to this method is made.
        *
        * @return String representation of the configuration
        */
        public static string FetchConfig()
        {
            EnsureSDKInitialized("FetchConfig");
            LogTrace(TAG + "FetchConfig start");
            string config = ExecuteWithNativeState(ApproovBridge.FetchConfig);
            return config;
        }
        /**
        * Sets a development key on the SDK. This may provide a key indicating that
        * the app is a development version and it should pass attestation even
        * if the app is not registered or it is running on an emulator. The development
        * key value can be rotated at any point in the account if a version of the app
        * containing the development key is accidentally released. This is primarily
        * used for situations where the app package must be modified or resigned in
        * some way as part of the testing process.
        *
        * @param key is the development key value to be set, which may be null
        */
        public static void SetDevKey(string key) {
            LogTrace(TAG + "SetDevKey called keyPresent=" + !string.IsNullOrWhiteSpace(key) + " sdkInitialized=" + IsSDKInitialized());
            ApproovBridge.SetDevKey(key);
        }

        /**
        * Obtains an integrity measurement proof that is used to show that the app and its
        * environment have not changed since the time of the original integrity measurement.
        * The proof does an HMAC calculation over the secret integrity measurement value which
        * is salted by a provided nonce. This proves that the SDK is able to reproduce the
        * integrity measurement value.
        *
        * @param nonce is a 16-byte (128-bit) nonce value used to salt the proof HMAC
        * @param measurementConfig is the measurement configuration obtained from a previous token fetch results
        * @return 32-byte (256-bit) measurement proof value
        */
        public static byte[] GetIntegrityMeasurementProof(byte[] nonce, byte[] measurementConfig) {
            byte[] proof = ApproovBridge.GetIntegrityMeasurementProof(nonce, measurementConfig);
            return proof;
        }

        /**
        * Obtains a device measurement proof that is used to show that the device environment
        * has not changed since the time of the original integrity measurement. This allows the
        * app version, including the Approov SDK, to be updated while preserving the device
        * measurement. The proof does an HMAC calculation over the secret device measurement
        * value which is salted by a provided nonce. This proves that the SDK is able to reproduce
        * the device measurement value.
        *
        * @param nonce is a 16-byte (128-bit) nonce value used to salt the proof HMAC
        * @param measurementConfig is the measurement configuration obtained from a previous token fetch results
        * @return 32-byte (256-bit) measurement proof value
        */
        public static byte[] GetDeviceMeasurementProof(byte[] nonce, byte[] measurementConfig) {
            byte[] proof = ApproovBridge.GetDeviceMeasurementProof(nonce, measurementConfig);
            return proof;
        }

        internal static bool ShouldApplyPinning(ApproovRequestContext request)
        {
            return GetServiceMutator().ShouldProcessPinning(request);
        }
        // MARK: END Approov API related methods
    }// ApproovService class

}// namespace Approov
