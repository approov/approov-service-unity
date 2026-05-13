using System;
using UnityEngine.Networking;

namespace Approov
{
    /// <summary>
    /// Compatibility wrapper over <see cref="UnityWebRequest"/> that attaches Approov-aware
    /// certificate validation and forwards send operations through <see cref="ApproovService"/>.
    /// </summary>
    /// <remarks>
    /// New integrations should generally prefer <see cref="UnityWebRequestApproovExtensions.SendApproovWebRequest(UnityWebRequest)"/>
    /// because Unity method hiding can bypass this wrapper when the instance is later treated as a
    /// plain <see cref="UnityWebRequest"/>.
    /// </remarks>
    public class ApproovWebRequest : UnityWebRequest
    {
        public ApproovWebRequest() : base()
        {
            this.certificateHandler = new ApproovCertificateHandler(this);
        }

        public ApproovWebRequest(string url) : base(url)
        {
            this.certificateHandler = new ApproovCertificateHandler(this);
        }

        public ApproovWebRequest(Uri uri) : base(uri)
        {
            this.certificateHandler = new ApproovCertificateHandler(this);
        }

        public ApproovWebRequest(string url, string method) : base(url, method)
        {
            this.certificateHandler = new ApproovCertificateHandler(this);
        }

        public ApproovWebRequest(Uri uri, string method) : base(uri, method)
        {
            this.certificateHandler = new ApproovCertificateHandler(this);
        }

        public ApproovWebRequest(string url, string method, DownloadHandler downloadHandler, UploadHandler uploadHandler) : base(url, method, downloadHandler, uploadHandler)
        {
            this.certificateHandler = new ApproovCertificateHandler(this);
        }

        public ApproovWebRequest(Uri uri, string method, DownloadHandler downloadHandler, UploadHandler uploadHandler) : base(uri, method, downloadHandler, uploadHandler)
        {
            this.certificateHandler = new ApproovCertificateHandler(this);
        }

        public new static void ClearCookieCache()
        {
            UnityWebRequest.ClearCookieCache();
        }

        public new static ApproovWebRequest Delete(string uri)
        {
            return new ApproovWebRequest(uri, "DELETE");
        }

        public new static ApproovWebRequest Get(string uri)
        {
            return new ApproovWebRequest(uri, "GET");
        }

        public new static ApproovWebRequest Get(Uri uri)
        {
            return new ApproovWebRequest(uri, "GET");
        }

        public new static ApproovWebRequest Head(string uri)
        {
            return new ApproovWebRequest(uri, "HEAD");
        }

        public new static ApproovWebRequest Head(Uri uri)
        {
            return new ApproovWebRequest(uri, "HEAD");
        }

        public static ApproovWebRequest Post(string uri)
        {
            return new ApproovWebRequest(uri, "POST");
        }

        public static ApproovWebRequest Post(Uri uri)
        {
            return new ApproovWebRequest(uri, "POST");
        }

        public new static ApproovWebRequest PostWwwForm(string uri, string form)
        {
            ApproovWebRequest request = new ApproovWebRequest(uri, "POST");
            byte[] formData = System.Text.Encoding.UTF8.GetBytes(form);
            request.uploadHandler = new UploadHandlerRaw(formData);
            request.uploadHandler.contentType = "application/x-www-form-urlencoded";
            return request;
        }

        public new static ApproovWebRequest PostWwwForm(Uri uri, string form)
        {
            ApproovWebRequest request = new ApproovWebRequest(uri, "POST");
            byte[] formData = System.Text.Encoding.UTF8.GetBytes(form);
            request.uploadHandler = new UploadHandlerRaw(formData);
            request.uploadHandler.contentType = "application/x-www-form-urlencoded";
            return request;
        }

        public static ApproovWebRequest Put(string uri)
        {
            return new ApproovWebRequest(uri, "PUT");
        }

        public static ApproovWebRequest Put(Uri uri)
        {
            return new ApproovWebRequest(uri, "PUT");
        }

        /// <summary>
        /// Sends the request through <see cref="ApproovService"/> so the full request-processing
        /// pipeline runs before Unity dispatches the network call.
        /// </summary>
        public new UnityWebRequestAsyncOperation SendWebRequest()
        {
            return ApproovService.SendWebRequest(this);
        }
    }
}
