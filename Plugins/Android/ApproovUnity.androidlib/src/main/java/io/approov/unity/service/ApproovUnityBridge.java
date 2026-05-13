package io.approov.unity.service;

import android.app.Activity;
import android.app.Application;
import android.content.Context;
import android.util.Base64;

import com.criticalblue.approovsdk.Approov;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayInputStream;
import java.net.URI;
import java.net.URL;
import java.security.MessageDigest;
import java.security.PublicKey;
import java.security.cert.Certificate;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.locks.ReadWriteLock;
import java.util.concurrent.locks.ReentrantReadWriteLock;

import javax.net.ssl.HttpsURLConnection;

public final class ApproovUnityBridge {
    private static final String TAG = "ApproovUnityBridge: ";
    private static final int FETCH_CERTIFICATES_TIMEOUT_MS = 3000;
    private static final String SUCCESS = "SUCCESS";
    private static final String INTERNAL_ERROR_RESULT_JSON = "{\"status\":11,\"statusString\":\"INTERNAL_ERROR\"}";
    private static final int APPROOV_CERT_CACHE_SIZE = 10;
    private static final HashMap<String, byte[]> approovCertCache = new HashMap<>(APPROOV_CERT_CACHE_SIZE);
    private static final ReadWriteLock certCacheLock = new ReentrantReadWriteLock();

    private ApproovUnityBridge() {
    }

    public static void initialize(String config) {
        Context applicationContext = getApplicationContext();
        if (applicationContext == null) {
            throw new IllegalStateException("Unable to resolve an Android application context for Approov initialization");
        }
        Approov.initialize(applicationContext, config, "auto", null);
    }

    public static String fetchConfig() {
        return Approov.fetchConfig();
    }

    public static String getPinsJSON(String pinType) {
        return Approov.getPinsJSON(pinType);
    }

    public static String fetchApproovTokenAndWait(String url) {
        return tokenFetchResultToJson(Approov.fetchApproovTokenAndWait(url));
    }

    public static String fetchCustomJWTAndWait(String payload) {
        return tokenFetchResultToJson(Approov.fetchCustomJWTAndWait(payload));
    }

    public static String fetchSecureStringAndWait(String key, String newDef) {
        return tokenFetchResultToJson(Approov.fetchSecureStringAndWait(key, newDef));
    }

    public static void setUserProperty(String property) {
        Approov.setUserProperty(property);
    }

    public static void setActivity(Activity activity) {
        Approov.setActivity(activity);
    }

    private static Context getApplicationContext() {
        Activity activity = getCurrentUnityActivity();
        if (activity != null) {
            return activity.getApplicationContext();
        }

        try {
            // Avoid a hard compile-time dependency on UnityPlayer so the Android library can compile
            // cleanly in generated Gradle projects where that class is not on this module classpath.
            Class<?> activityThreadClass = Class.forName("android.app.ActivityThread");
            Object application = activityThreadClass.getMethod("currentApplication").invoke(null);
            if (application instanceof Application) {
                return ((Application) application).getApplicationContext();
            }
        } catch (Exception ignored) {
            // Fall through and return null below.
        }

        return null;
    }

    private static Activity getCurrentUnityActivity() {
        try {
            Class<?> unityPlayerClass = Class.forName("com.unity3d.player.UnityPlayer");
            Object currentActivity = unityPlayerClass.getField("currentActivity").get(null);
            if (currentActivity instanceof Activity) {
                return (Activity) currentActivity;
            }
        } catch (Exception ignored) {
            // Fall through and return null below.
        }

        return null;
    }

    public static void setDevKey(String key) {
        Approov.setDevKey(key);
    }

    public static void setDataHashInToken(String data) {
        Approov.setDataHashInToken(data);
    }

    public static byte[] getIntegrityMeasurementProof(byte[] nonce, byte[] measurementConfig) {
        return Approov.getIntegrityMeasurementProof(nonce, measurementConfig);
    }

    public static byte[] getDeviceMeasurementProof(byte[] nonce, byte[] measurementConfig) {
        return Approov.getDeviceMeasurementProof(nonce, measurementConfig);
    }

    public static String getDeviceID() {
        return Approov.getDeviceID();
    }

    public static String getMessageSignature(String message) {
        return getAccountMessageSignature(message);
    }

    public static String getAccountMessageSignature(String message) {
        return Approov.getAccountMessageSignature(message);
    }

    public static String getInstallMessageSignature(String message) {
        return Approov.getInstallMessageSignature(message);
    }

    public static void clearCertificateCache() {
        certCacheLock.writeLock().lock();
        try {
            approovCertCache.clear();
        } finally {
            certCacheLock.writeLock().unlock();
        }
    }

    public static String shouldProceedWithConnection(byte[] cert, String hostname, String pinType) {
        String pinningString = getCertPinForPinType(cert, pinType);
        if (pinningString == null) {
            return TAG + "Unable to extract pin value from certificate for host " + hostname;
        }

        if (checkPinForHostIsSetInApproov(hostname, pinningString, getPinsForHost(hostname, pinType))) {
            return SUCCESS;
        }

        byte[] cachedCert = getFromCache(hostname);
        if (cachedCert != null) {
            if (Arrays.equals(cachedCert, cert)) {
                return SUCCESS;
            }

            removeFromCache(hostname);
        }

        List<byte[]> hostCertificates = fetchHostCertificates(hostname);
        if (hostCertificates == null) {
            return TAG + "Unable to fetch certificates for host " + hostname;
        }

        if (hostCertificates.size() < 2) {
            return TAG + "Certificate chain too small for host " + hostname;
        }

        if (!Arrays.equals(cert, hostCertificates.get(0))) {
            return TAG + "Leaf certificate presented does not match the one fetched for host " + hostname;
        }

        String resultMessage = approovPinsValidation(hostCertificates, hostname, pinType);
        if (resultMessage == null) {
            addToCache(hostname, hostCertificates.get(0));
            return SUCCESS;
        }

        return resultMessage;
    }

    private static String tokenFetchResultToJson(Approov.TokenFetchResult result) {
        if (result == null) {
            return INTERNAL_ERROR_RESULT_JSON;
        }

        JSONObject json = new JSONObject();
        try {
            json.put("status", result.getStatus().ordinal());
            json.put("statusString", result.getStatus().toString());
            json.put("ARC", result.getARC());
            json.put("isForceApplyPins", result.isForceApplyPins());
            json.put("token", result.getToken());
            json.put("traceID", result.getTraceID());
            json.put("rejectionReasons", result.getRejectionReasons());
            json.put("isConfigChanged", result.isConfigChanged());
            json.put("secureString", result.getSecureString());
            json.put("loggableToken", result.getLoggableToken());

            byte[] measurementConfig = result.getMeasurementConfig();
            if (measurementConfig != null) {
                JSONArray measurementArray = new JSONArray();
                for (byte value : measurementConfig) {
                    measurementArray.put(value & 0xFF);
                }
                json.put("measurementConfig", measurementArray);
            }
        } catch (JSONException exception) {
            return INTERNAL_ERROR_RESULT_JSON;
        }

        return json.toString();
    }

    private static List<byte[]> fetchHostCertificates(String hostname) {
        try {
            URI uri = new URI("https", hostname, null, null);
            URL url = uri.toURL();
            HttpsURLConnection connection = (HttpsURLConnection) url.openConnection();
            connection.setConnectTimeout(FETCH_CERTIFICATES_TIMEOUT_MS);
            connection.connect();
            Certificate[] certificates = connection.getServerCertificates();
            List<byte[]> hostCertificates = new ArrayList<>(certificates.length);
            for (Certificate certificate : certificates) {
                hostCertificates.add(certificate.getEncoded());
            }
            connection.disconnect();
            return hostCertificates;
        } catch (Exception exception) {
            return null;
        }
    }

    private static String approovPinsValidation(List<byte[]> hostCertificates, String hostname, String pinType) {
        int startIndex = 1;
        List<String> allPinsForHost = getPinsForHost(hostname, pinType);
        if (allPinsForHost != null && allPinsForHost.size() == 0) {
            allPinsForHost = getPinsForHost("*", pinType);
            startIndex = hostCertificates.size() - 1;
        }

        if (allPinsForHost == null || allPinsForHost.size() == 0) {
            return null;
        }

        for (int index = startIndex; index < hostCertificates.size(); index++) {
            String evaluatedPin = getCertPinForPinType(hostCertificates.get(index), pinType);
            if (evaluatedPin == null) {
                return TAG + "Unable to extract pin value for intermediate/root cert for host " + hostname;
            }

            if (checkPinForHostIsSetInApproov(hostname, evaluatedPin, allPinsForHost)) {
                return null;
            }
        }

        return TAG + "No matching Intermediate/root cert pins for host " + hostname;
    }

    private static String getCertPinForPinType(byte[] cert, String pinType) {
        X509Certificate x509Certificate;
        try {
            CertificateFactory certificateFactory = CertificateFactory.getInstance("X.509");
            x509Certificate = (X509Certificate) certificateFactory.generateCertificate(new ByteArrayInputStream(cert));
        } catch (Exception exception) {
            return null;
        }

        if ("public-key-sha256".equals(pinType)) {
            return publicKeyWithHeader(x509Certificate);
        }

        return null;
    }

    private static String publicKeyWithHeader(X509Certificate cert) {
        try {
            PublicKey publicKey = cert.getPublicKey();
            byte[] encoded = publicKey.getEncoded();
            if (encoded == null) {
                return null;
            }

            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] hash = digest.digest(encoded);
            return Base64.encodeToString(hash, Base64.NO_WRAP);
        } catch (Exception exception) {
            return null;
        }
    }

    private static boolean checkPinForHostIsSetInApproov(String host, String targetPin, List<String> pinsForHost) {
        if (pinsForHost == null || pinsForHost.size() == 0) {
            return false;
        }

        return pinsForHost.contains(targetPin);
    }

    private static void addToCache(String key, byte[] value) {
        certCacheLock.writeLock().lock();
        try {
            approovCertCache.put(key, value);
        } finally {
            certCacheLock.writeLock().unlock();
        }
    }

    private static byte[] getFromCache(String key) {
        certCacheLock.readLock().lock();
        try {
            return approovCertCache.get(key);
        } finally {
            certCacheLock.readLock().unlock();
        }
    }

    private static void removeFromCache(String key) {
        certCacheLock.writeLock().lock();
        try {
            approovCertCache.remove(key);
        } finally {
            certCacheLock.writeLock().unlock();
        }
    }

    private static List<String> getPinsForHost(String hostname, String pinType) {
        Map<String, List<String>> allPins = Approov.getPins(pinType);
        if (allPins == null) {
            return null;
        }

        return allPins.get(hostname);
    }
}
