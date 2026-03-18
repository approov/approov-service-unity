using System;
using UnityEngine.Networking;

namespace Approov
{
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

        public new UnityWebRequestAsyncOperation SendWebRequest()
        {
            ApproovRequestProcessor.ApplyToUnityWebRequest(this);
            if (this.downloadHandler == null)
            {
                this.downloadHandler = new DownloadHandlerBuffer();
            }

            return base.SendWebRequest();
        }
    }
}
