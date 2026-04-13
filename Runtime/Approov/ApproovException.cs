using System;

namespace Approov
{
    /// <summary>
    /// Base exception type for service-layer failures surfaced by the Unity package.
    /// </summary>
    public class ApproovException : Exception
    {
        /// <summary>
        /// Indicates whether the failure is generally safe to retry after a delay.
        /// </summary>
        public bool ShouldRetry;

        public ApproovException()
            : this("ApproovException: Unknown Error.")
        {
        }

        public ApproovException(string message) : base(message)
        {
            ShouldRetry = false;
        }

        public ApproovException(string message, bool shouldRetry) : base(message)
        {
            ShouldRetry = shouldRetry;
        }
    }

    /// <summary>
    /// Indicates a failure while initializing the native Approov SDK.
    /// </summary>
    public class InitializationFailureException : ApproovException
    {
        public InitializationFailureException(string message)
            : base(message)
        {
        }

        public InitializationFailureException(string message, bool shouldRetry)
            : base(message, shouldRetry)
        {
        }
    }

    /// <summary>
    /// Indicates an invalid or incompatible local configuration.
    /// </summary>
    public class ConfigurationFailureException : ApproovException
    {
        public ConfigurationFailureException(string message)
            : base(message)
        {
        }

        public ConfigurationFailureException(string message, bool shouldRetry)
            : base(message, shouldRetry)
        {
        }
    }

    /// <summary>
    /// Indicates a pinning-related failure.
    /// </summary>
    public class PinningErrorException : ApproovException
    {
        public PinningErrorException(string message)
            : base(message)
        {
        }

        public PinningErrorException(string message, bool shouldRetry)
            : base(message, shouldRetry)
        {
        }
    }

    /// <summary>
    /// Indicates a transient networking problem while talking to Approov services.
    /// </summary>
    public class NetworkingErrorException : ApproovException
    {
        public NetworkingErrorException(string message)
            : base(message)
        {
        }

        public NetworkingErrorException(string message, bool shouldRetry)
            : base(message, shouldRetry)
        {
        }
    }

    /// <summary>
    /// Indicates a non-retryable failure returned by the SDK or service layer.
    /// </summary>
    public class PermanentException : ApproovException
    {
        public PermanentException(string message)
            : base(message)
        {
        }

        public PermanentException(string message, bool shouldRetry)
            : base(message, shouldRetry)
        {
        }
    }

    /// <summary>
    /// Indicates that attestation or a protected fetch was explicitly rejected.
    /// </summary>
    public class RejectionException : ApproovException
    {
        /// <summary>
        /// Approov rejection code describing the rejection class.
        /// </summary>
        public string ARC;

        /// <summary>
        /// Optional rejection reasons returned by the SDK for diagnostics or UX messaging.
        /// </summary>
        public string RejectionReasons;

        public RejectionException(string message, string arc, string rejectionReasons)
            : base(message)
        {
            ShouldRetry = false;
            ARC = arc;
            RejectionReasons = rejectionReasons;
        }
    }
}
