using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Approov
{
    public enum ApproovRequestTransport
    {
        UnityWebRequest,
        HttpClient,
        Snapshot
    }

    public sealed class ApproovRequestContext
    {
        private const string TAG = "ApproovRequestContext ";
        private readonly Func<string, string> _getHeader;
        private readonly Action<string, string> _setHeader;
        private readonly Action<Uri> _setUri;
        private readonly Func<byte[]> _getBodyBytes;
        private readonly Dictionary<string, string> _snapshotHeaders;
        private readonly byte[] _snapshotBody;
        private Uri _uri;

        private ApproovRequestContext(
            ApproovRequestTransport transport,
            string method,
            Uri uri,
            Func<string, string> getHeader,
            Action<string, string> setHeader,
            Action<Uri> setUri,
            Func<byte[]> getBodyBytes,
            Dictionary<string, string> snapshotHeaders,
            byte[] snapshotBody)
        {
            Transport = transport;
            Method = method ?? string.Empty;
            _uri = uri;
            _getHeader = getHeader;
            _setHeader = setHeader;
            _setUri = setUri;
            _getBodyBytes = getBodyBytes;
            _snapshotHeaders = snapshotHeaders;
            _snapshotBody = snapshotBody;
        }

        public ApproovRequestTransport Transport { get; }

        public string Method { get; }

        public Uri Uri
        {
            get => _uri;
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                _setUri?.Invoke(value);
                _uri = value;
            }
        }

        public string GetHeader(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (_snapshotHeaders != null)
            {
                return _snapshotHeaders.TryGetValue(name, out string snapshotValue) ? snapshotValue : null;
            }

            return _getHeader?.Invoke(name);
        }

        public bool HasHeader(string name)
        {
            // Header presence is independent from whether the stored value is empty.
            return GetHeader(name) != null;
        }

        public void SetHeader(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            value ??= string.Empty;
            _setHeader?.Invoke(name, value);

            if (_snapshotHeaders != null)
            {
                _snapshotHeaders[name] = value;
            }
        }

        public bool TryGetBodyBytes(out byte[] bodyBytes)
        {
            byte[] bytes = _snapshotBody;
            if (bytes == null && _getBodyBytes != null)
            {
                bytes = _getBodyBytes.Invoke();
            }

            if (bytes == null || bytes.Length == 0)
            {
                bodyBytes = null;
                return false;
            }

            bodyBytes = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, bodyBytes, 0, bytes.Length);
            return true;
        }

        internal static ApproovRequestContext Create(UnityWebRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return new ApproovRequestContext(
                ApproovRequestTransport.UnityWebRequest,
                request.method,
                request.uri ?? CreateUri(request.url),
                request.GetRequestHeader,
                request.SetRequestHeader,
                uri => request.uri = uri,
                () => TryGetUnityBodyBytes(request),
                null,
                null);
        }

        internal static ApproovRequestContext Create(HttpRequestMessage request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return new ApproovRequestContext(
                ApproovRequestTransport.HttpClient,
                request.Method?.Method,
                request.RequestUri,
                header => GetHttpHeader(request, header),
                (header, value) => SetHttpHeader(request, header, value),
                uri => request.RequestUri = uri,
                () => TryGetHttpBodyBytes(request),
                null,
                null);
        }

        internal static ApproovRequestContext CreateSnapshot(UnityWebRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            // UnityWebRequest does not expose a way to enumerate outbound request headers, so snapshots
            // cannot fully clone header state like HttpRequestMessage can. The live request context created
            // by Create(UnityWebRequest) still resolves headers through GetRequestHeader while the request
            // is mutable on the main thread; snapshots intentionally preserve only the request metadata and
            // any readable body bytes needed off-thread, such as certificate validation decisions.

            return new ApproovRequestContext(
                ApproovRequestTransport.Snapshot,
                request.method,
                request.uri ?? CreateUri(request.url),
                null,
                null,
                null,
                null,
                headers,
                TryGetUnityBodyBytes(request));
        }

        internal static ApproovRequestContext CreateSnapshot(HttpRequestMessage request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.Generic.KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                headers[header.Key] = CombineHeaderValues(header.Value);
            }

            if (request.Content != null)
            {
                foreach (System.Collections.Generic.KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                {
                    headers[header.Key] = CombineHeaderValues(header.Value);
                }
            }

            return new ApproovRequestContext(
                ApproovRequestTransport.Snapshot,
                request.Method?.Method,
                request.RequestUri,
                null,
                null,
                null,
                null,
                headers,
                TryGetHttpBodyBytes(request));
        }

        internal static string CombineHeaderValues(IEnumerable<string> values)
        {
            if (values == null)
            {
                return null;
            }

            StringBuilder builder = new();
            bool isFirst = true;
            foreach (string value in values)
            {
                if (!isFirst)
                {
                    builder.Append(',');
                }

                builder.Append(value?.Trim() ?? string.Empty);
                isFirst = false;
            }

            return builder.Length == 0 ? null : builder.ToString();
        }

        private static byte[] TryGetUnityBodyBytes(UnityWebRequest request)
        {
            try
            {
                UploadHandler uploadHandler = request.uploadHandler;
                return uploadHandler?.data;
            }
            catch
            {
                return null;
            }
        }

        private static byte[] TryGetHttpBodyBytes(HttpRequestMessage request)
        {
            if (request?.Content == null)
            {
                return null;
            }

            try
            {
                request.Content.LoadIntoBufferAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                return request.Content.ReadAsByteArrayAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (IOException ex)
            {
                Debug.LogWarning(TAG + "TryGetHttpBodyBytes failed to read buffered content: " + ex.Message);
                return null;
            }
            catch (TaskCanceledException ex)
            {
                Debug.LogWarning(TAG + "TryGetHttpBodyBytes was canceled: " + ex.Message);
                return null;
            }
            catch (ObjectDisposedException ex)
            {
                Debug.LogWarning(TAG + "TryGetHttpBodyBytes cannot read disposed content: " + ex.Message);
                return null;
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning(TAG + "TryGetHttpBodyBytes cannot buffer content: " + ex.Message);
                return null;
            }
        }

        private static string GetHttpHeader(HttpRequestMessage request, string header)
        {
            if (request.Headers.TryGetValues(header, out IEnumerable<string> values))
            {
                return CombineHeaderValues(values);
            }

            if (request.Content != null && request.Content.Headers.TryGetValues(header, out values))
            {
                return CombineHeaderValues(values);
            }

            return null;
        }

        private static void SetHttpHeader(HttpRequestMessage request, string header, string value)
        {
            request.Headers.Remove(header);
            request.Content?.Headers.Remove(header);

            if (!request.Headers.TryAddWithoutValidation(header, value) && request.Content != null)
            {
                request.Content.Headers.TryAddWithoutValidation(header, value);
            }
        }

        private static Uri CreateUri(string url)
        {
            return string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? null : uri;
        }
    }
}
