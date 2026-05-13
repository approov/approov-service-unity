using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Approov
{
    /// <summary>
    /// Normalized result returned by native token, secure-string, and custom JWT fetch operations.
    /// </summary>
    [Serializable]
    public struct ApproovTokenFetchResult
    {
        public ApproovTokenFetchStatus status;
        public string ARC;
        public bool isForceApplyPins;
        public string token;
        public string traceID;
        public string rejectionReasons;
        public bool isConfigChanged;
        public string secureString;
        public byte[] measurementConfig;
        public string loggableToken;
    }

    [Serializable]
    internal sealed class ApproovTokenFetchResultPayload
    {
        public int status;
        public string statusString;
        public string ARC;
        public bool isForceApplyPins;
        public string token;
        public string traceID;
        public string rejectionReasons;
        public bool isConfigChanged;
        public string secureString;
        public int[] measurementConfig;
        public string loggableToken;
    }

    /// <summary>
    /// Fetch status values returned by the native Approov SDK.
    /// </summary>
    public enum ApproovTokenFetchStatus
    {
        Success,
        NoNetwork,
        MITMDetected,
        PoorNetwork,
        NoApproovService,
        BadURL,
        UnknownURL,
        UnprotectedURL,
        NotInitialized,
        Rejected,
        Disabled,
        UnknownKey,
        BadKey,
        BadPayload,
        InternalError,
        NoNetworkPermission,
        MissingLibDependency
    }

    internal static class ApproovBridge
    {
        public static readonly string TAG = "ApproovBridge: ";
        private static readonly string SUCCESS = "SUCCESS";
        public static readonly string kPinTypePublicKeySha256 = "public-key-sha256";

        private static ApproovTokenFetchResult DeserializeFetchResult(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new ApproovTokenFetchResult
                {
                    status = ApproovTokenFetchStatus.InternalError,
                };
            }

            ApproovTokenFetchResultPayload payload = JsonUtility.FromJson<ApproovTokenFetchResultPayload>(json);
            if (payload == null)
            {
                return new ApproovTokenFetchResult
                {
                    status = ApproovTokenFetchStatus.InternalError,
                };
            }

            byte[] measurementConfig = null;
            if (payload.measurementConfig != null)
            {
                // JsonUtility does not deserialize directly into byte[] from JSON number arrays, so
                // normalize through int[] first and then convert to the public byte[] contract.
                measurementConfig = Array.ConvertAll(payload.measurementConfig, value => (byte)value);
            }

            return new ApproovTokenFetchResult
            {
                status = ConvertTokenFetchStatus(payload.statusString, payload.status),
                ARC = payload.ARC,
                isForceApplyPins = payload.isForceApplyPins,
                token = payload.token,
                traceID = payload.traceID,
                rejectionReasons = payload.rejectionReasons,
                isConfigChanged = payload.isConfigChanged,
                secureString = payload.secureString,
                measurementConfig = measurementConfig,
                loggableToken = payload.loggableToken,
            };
        }

        private static ApproovTokenFetchStatus ConvertTokenFetchStatus(string statusString, int status)
        {
            if (!string.IsNullOrWhiteSpace(statusString))
            {
                switch (statusString.Trim().ToUpperInvariant())
                {
                    case "SUCCESS":
                        return ApproovTokenFetchStatus.Success;
                    case "NO_NETWORK":
                        return ApproovTokenFetchStatus.NoNetwork;
                    case "MITM_DETECTED":
                        return ApproovTokenFetchStatus.MITMDetected;
                    case "POOR_NETWORK":
                        return ApproovTokenFetchStatus.PoorNetwork;
                    case "NO_APPROOV_SERVICE":
                        return ApproovTokenFetchStatus.NoApproovService;
                    case "BAD_URL":
                        return ApproovTokenFetchStatus.BadURL;
                    case "UNKNOWN_URL":
                        return ApproovTokenFetchStatus.UnknownURL;
                    case "UNPROTECTED_URL":
                        return ApproovTokenFetchStatus.UnprotectedURL;
                    case "NOT_INITIALIZED":
                        return ApproovTokenFetchStatus.NotInitialized;
                    case "NO_NETWORK_PERMISSION":
                        return ApproovTokenFetchStatus.NoNetworkPermission;
                    case "MISSING_LIB_DEPENDENCY":
                        return ApproovTokenFetchStatus.MissingLibDependency;
                    case "INTERNAL_ERROR":
                        return ApproovTokenFetchStatus.InternalError;
                    case "REJECTED":
                        return ApproovTokenFetchStatus.Rejected;
                    case "DISABLED":
                        return ApproovTokenFetchStatus.Disabled;
                    case "UNKNOWN_KEY":
                        return ApproovTokenFetchStatus.UnknownKey;
                    case "BAD_KEY":
                        return ApproovTokenFetchStatus.BadKey;
                    case "BAD_PAYLOAD":
                        return ApproovTokenFetchStatus.BadPayload;
                }
            }

            return ConvertTokenFetchStatus(status);
        }

        private static ApproovTokenFetchStatus ConvertTokenFetchStatus(int status)
        {
            return status switch
            {
                0 => ApproovTokenFetchStatus.Success,
                1 => ApproovTokenFetchStatus.NoNetwork,
                2 => ApproovTokenFetchStatus.MITMDetected,
                3 => ApproovTokenFetchStatus.PoorNetwork,
                4 => ApproovTokenFetchStatus.NoApproovService,
                5 => ApproovTokenFetchStatus.BadURL,
                6 => ApproovTokenFetchStatus.UnknownURL,
                7 => ApproovTokenFetchStatus.UnprotectedURL,
                8 => ApproovTokenFetchStatus.NotInitialized,
                9 => ApproovTokenFetchStatus.NoNetworkPermission,
                10 => ApproovTokenFetchStatus.MissingLibDependency,
                11 => ApproovTokenFetchStatus.InternalError,
                12 => ApproovTokenFetchStatus.Rejected,
                13 => ApproovTokenFetchStatus.Disabled,
                14 => ApproovTokenFetchStatus.UnknownKey,
                15 => ApproovTokenFetchStatus.BadKey,
                16 => ApproovTokenFetchStatus.BadPayload,
                _ => ApproovTokenFetchStatus.InternalError,
            };
        }

#if UNITY_IOS
        [DllImport("__Internal")]
        private static extern IntPtr Approov_fetchConfig();

        [DllImport("__Internal")]
        private static extern IntPtr Approov_getPinsJSON(string pinType);

        [DllImport("__Internal")]
        private static extern bool Approov_initialize(string initialConfig, string updateConfig, string comment, out IntPtr error);

        [DllImport("__Internal")]
        private static extern IntPtr Approov_fetchApproovTokenAndWait(string url);

        [DllImport("__Internal")]
        private static extern IntPtr Approov_fetchCustomJWTAndWait(string payload);

        [DllImport("__Internal")]
        private static extern IntPtr Approov_fetchSecureStringAndWait(string key, string newDef);

        [DllImport("__Internal")]
        private static extern void Approov_setUserProperty(string property);

        [DllImport("__Internal")]
        private static extern void Approov_setDevKey(string key);

        [DllImport("__Internal")]
        private static extern void Approov_setDataHashInToken(string data);

        [DllImport("__Internal")]
        private static extern IntPtr Approov_getIntegrityMeasurementProof(byte[] nonce, int nonceLength, byte[] measurementConfig, int measurementConfigLength, out int resultLength);

        [DllImport("__Internal")]
        private static extern IntPtr Approov_getDeviceMeasurementProof(byte[] nonce, int nonceLength, byte[] measurementConfig, int measurementConfigLength, out int resultLength);

        [DllImport("__Internal")]
        private static extern IntPtr Approov_getDeviceID();

        [DllImport("__Internal")]
        private static extern IntPtr Approov_getAccountMessageSignature(byte[] message, int messageLength);

        [DllImport("__Internal")]
        private static extern IntPtr Approov_getInstallMessageSignature(byte[] message, int messageLength);

        [DllImport("__Internal")]
        private static extern void Approov_emptyGlobalCacheDictionary();

        [DllImport("__Internal")]
        private static extern IntPtr Approov_shouldProceedWithConnection(byte[] cert, int certLength, byte[] hostname, int hostnameLength, byte[] pinType, int pinTypeLength);

        private static string StringFromNative(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            string result = Marshal.PtrToStringAnsi(ptr);
            Marshal.FreeHGlobal(ptr);
            return result;
        }

        public static bool Initialize(string initialConfig, string updateConfig, string comment, out IntPtr error)
        {
            return Approov_initialize(initialConfig, updateConfig, comment, out error);
        }

        public static string FetchConfig()
        {
            return StringFromNative(Approov_fetchConfig());
        }

        public static string GetPinsJSON(string pinType)
        {
            return StringFromNative(Approov_getPinsJSON(pinType));
        }

        public static ApproovTokenFetchResult FetchApproovTokenAndWait(string url)
        {
            return DeserializeFetchResult(StringFromNative(Approov_fetchApproovTokenAndWait(url)));
        }

        public static ApproovTokenFetchResult FetchCustomJWTAndWait(string payload)
        {
            return DeserializeFetchResult(StringFromNative(Approov_fetchCustomJWTAndWait(payload)));
        }

        public static ApproovTokenFetchResult FetchSecureStringAndWait(string key, string newDef = null)
        {
            return DeserializeFetchResult(StringFromNative(Approov_fetchSecureStringAndWait(key, newDef)));
        }

        public static void SetUserProperty(string property)
        {
            Approov_setUserProperty(property);
        }

        public static void SetDevKey(string key)
        {
            Approov_setDevKey(key);
        }

        public static void SetDataHashInToken(string data)
        {
            Approov_setDataHashInToken(data);
        }

        public static byte[] GetIntegrityMeasurementProof(byte[] nonce, byte[] measurementConfig)
        {
            if (nonce == null || measurementConfig == null)
            {
                return null;
            }

            IntPtr resultPtr = Approov_getIntegrityMeasurementProof(nonce, nonce.Length, measurementConfig, measurementConfig.Length, out int resultLength);
            if (resultPtr == IntPtr.Zero || resultLength <= 0)
            {
                return null;
            }

            byte[] result = new byte[resultLength];
            Marshal.Copy(resultPtr, result, 0, resultLength);
            Marshal.FreeHGlobal(resultPtr);
            return result;
        }

        public static byte[] GetDeviceMeasurementProof(byte[] nonce, byte[] measurementConfig)
        {
            if (nonce == null || measurementConfig == null)
            {
                return null;
            }

            IntPtr resultPtr = Approov_getDeviceMeasurementProof(nonce, nonce.Length, measurementConfig, measurementConfig.Length, out int resultLength);
            if (resultPtr == IntPtr.Zero || resultLength <= 0)
            {
                return null;
            }

            byte[] result = new byte[resultLength];
            Marshal.Copy(resultPtr, result, 0, resultLength);
            Marshal.FreeHGlobal(resultPtr);
            return result;
        }

        public static string GetDeviceID()
        {
            return StringFromNative(Approov_getDeviceID());
        }

        public static string GetAccountMessageSignature(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            return StringFromNative(Approov_getAccountMessageSignature(messageBytes, messageBytes.Length));
        }

        public static string GetInstallMessageSignature(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            return StringFromNative(Approov_getInstallMessageSignature(messageBytes, messageBytes.Length));
        }

        public static void ClearCertificateCache()
        {
            Approov_emptyGlobalCacheDictionary();
        }

        public static string ShouldProceedWithNetworkConnection(byte[] cert, string url, string pinType)
        {
            byte[] urlBytes = Encoding.UTF8.GetBytes(url);
            byte[] pinTypeBytes = Encoding.UTF8.GetBytes(pinType);
            string result = StringFromNative(Approov_shouldProceedWithConnection(cert, cert.Length, urlBytes, urlBytes.Length, pinTypeBytes, pinTypeBytes.Length));
            return result == SUCCESS ? null : result;
        }
#elif UNITY_ANDROID
        private const string AndroidBridgeClassName = "io.approov.unity.service.ApproovUnityBridge";
        private static readonly object BridgeClassLock = new();
        private static AndroidJavaClass sBridgeClass;

        private static sbyte[] ToSignedBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            // Unity's Android JNI bridge now expects signed byte arrays for Java byte[] marshaling.
            sbyte[] signedBytes = new sbyte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, signedBytes, 0, bytes.Length);
            return signedBytes;
        }

        private static byte[] ToUnsignedBytes(sbyte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            // Convert the Java byte[] result back into the public C# byte[] shape.
            byte[] unsignedBytes = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, unsignedBytes, 0, bytes.Length);
            return unsignedBytes;
        }

        private static AndroidJavaClass BridgeClass
        {
            get
            {
                lock (BridgeClassLock)
                {
                    if (sBridgeClass == null)
                    {
                        AndroidJNI.AttachCurrentThread();
                        sBridgeClass = new AndroidJavaClass(AndroidBridgeClassName);
                    }

                    return sBridgeClass;
                }
            }
        }

        public static void Initialize(string config)
        {
            BridgeClass.CallStatic("initialize", config);
        }

        public static string FetchConfig()
        {
            return BridgeClass.CallStatic<string>("fetchConfig");
        }

        public static string GetPinsJSON(string pinType)
        {
            return BridgeClass.CallStatic<string>("getPinsJSON", pinType);
        }

        public static ApproovTokenFetchResult FetchApproovTokenAndWait(string url)
        {
            return DeserializeFetchResult(BridgeClass.CallStatic<string>("fetchApproovTokenAndWait", url));
        }

        public static ApproovTokenFetchResult FetchCustomJWTAndWait(string payload)
        {
            return DeserializeFetchResult(BridgeClass.CallStatic<string>("fetchCustomJWTAndWait", payload));
        }

        public static ApproovTokenFetchResult FetchSecureStringAndWait(string key, string newDef = null)
        {
            return DeserializeFetchResult(BridgeClass.CallStatic<string>("fetchSecureStringAndWait", key, newDef));
        }

        public static void SetUserProperty(string property)
        {
            BridgeClass.CallStatic("setUserProperty", property);
        }

        public static void SetActivity(AndroidJavaObject activity)
        {
            BridgeClass.CallStatic("setActivity", activity);
        }

        public static void SetDevKey(string key)
        {
            BridgeClass.CallStatic("setDevKey", key);
        }

        public static void SetDataHashInToken(string data)
        {
            BridgeClass.CallStatic("setDataHashInToken", data);
        }

        public static byte[] GetIntegrityMeasurementProof(byte[] nonce, byte[] measurementConfig)
        {
            return ToUnsignedBytes(BridgeClass.CallStatic<sbyte[]>(
                "getIntegrityMeasurementProof",
                ToSignedBytes(nonce),
                ToSignedBytes(measurementConfig)));
        }

        public static byte[] GetDeviceMeasurementProof(byte[] nonce, byte[] measurementConfig)
        {
            return ToUnsignedBytes(BridgeClass.CallStatic<sbyte[]>(
                "getDeviceMeasurementProof",
                ToSignedBytes(nonce),
                ToSignedBytes(measurementConfig)));
        }

        public static string GetDeviceID()
        {
            return BridgeClass.CallStatic<string>("getDeviceID");
        }

        public static string GetAccountMessageSignature(string message)
        {
            return BridgeClass.CallStatic<string>("getAccountMessageSignature", message);
        }

        public static string GetInstallMessageSignature(string message)
        {
            return BridgeClass.CallStatic<string>("getInstallMessageSignature", message);
        }

        public static void ClearCertificateCache()
        {
            BridgeClass.CallStatic("clearCertificateCache");
        }

        public static string ShouldProceedWithNetworkConnection(byte[] cert, string domain, string pinType)
        {
            string result = BridgeClass.CallStatic<string>("shouldProceedWithConnection", ToSignedBytes(cert), domain, pinType);
            return result == SUCCESS ? null : result;
        }
#else
        public static bool Initialize(string initialConfig, string updateConfig, string comment, out IntPtr error)
        {
            error = IntPtr.Zero;
            return false;
        }

        public static void Initialize(string config)
        {
        }

        public static string FetchConfig() => null;
        public static string GetPinsJSON(string pinType) => null;
        public static ApproovTokenFetchResult FetchApproovTokenAndWait(string url) => new() { status = ApproovTokenFetchStatus.InternalError };
        public static ApproovTokenFetchResult FetchCustomJWTAndWait(string payload) => new() { status = ApproovTokenFetchStatus.InternalError };
        public static ApproovTokenFetchResult FetchSecureStringAndWait(string key, string newDef = null) => new() { status = ApproovTokenFetchStatus.InternalError };
        public static void SetUserProperty(string property) {}
        public static void SetDevKey(string key) {}
        public static void SetDataHashInToken(string data) {}
        public static byte[] GetIntegrityMeasurementProof(byte[] nonce, byte[] measurementConfig) => null;
        public static byte[] GetDeviceMeasurementProof(byte[] nonce, byte[] measurementConfig) => null;
        public static string GetDeviceID() => null;
        public static string GetAccountMessageSignature(string message) => null;
        public static string GetInstallMessageSignature(string message) => null;
        public static void ClearCertificateCache() {}
        public static string ShouldProceedWithNetworkConnection(byte[] cert, string url, string pinType) => null;
#endif
    }
}
