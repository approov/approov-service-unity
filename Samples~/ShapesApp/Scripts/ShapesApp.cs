using System;
using System.Collections;
using System.Collections.Generic;
using Approov;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ShapesApp : MonoBehaviour
{
    public Button helloButton;
    public Button shapesButton;
    // The Text control to display the response
    public Text statusText;
    // The image UI control to display the image
    public Image shapesImage;
    // The API endpoint version: v1 unprotected (Api-Key header is required), v2 (Api-Key header protected)  and v3 protected by Approov token
    [SerializeField] private bool useApproov;
    static readonly string apiVersion = "v1";
    // The hello endpoint
    public static readonly string helloUrl = "https://shapes.approov.io/" + apiVersion + "/hello/";
    public static readonly string shapesUrl = "https://shapes.approov.io/" + apiVersion + "/shapes/";
    // The Dictionary holding image name to image data
    private Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();
    // The Api-Key for the Approov protected endpoint
    public static readonly string ApiKey = "yXClypapWNHIifHUWmBIyPFAm";
    // Start is called before the first frame update
    void Start()
    {
        // Assign the OnClick event to the buttons
        helloButton.onClick.AddListener(OnHelloButtonClicked);
        shapesButton.onClick.AddListener(OnShapesButtonClicked);
        // Load images from resources folder and populate the dictionary
        if (!LoadImageResources()) throw new Exception("Failed to load images");
        // Set default image at startup: "approov.png" from dictionary
        shapesImage.sprite = Sprite.Create(images["approov"], new Rect(0, 0, images["approov"].width, images["approov"].height), new Vector2(0.5f, 0.5f));
        if (useApproov)
        {
            try
            {
                ApproovService.Initialize();
            }
            catch (ConfigurationFailureException exception)
            {
                Debug.LogWarning(exception.Message);
            }
            catch (InitializationFailureException exception)
            {
                Debug.LogError(exception.Message);
            }
        }
    }

    Boolean LoadImageResources() {
        // Load images from resources folder and populate the dictionary
        Texture2D[] textures = Resources.LoadAll<Texture2D>("Images");
        if (textures.Length == 0)
        {
            Debug.LogError("No images found in the Resources/Images folder");
            return false;
        }
        foreach (Texture2D texture in textures)
        {
            images.Add(texture.name, texture);
        }
        return true;
    }

    public void OnHelloButtonClicked()
    {
        StartCoroutine(MakeGetRequest(helloUrl));
    }

    public void OnShapesButtonClicked()
    {
        StartCoroutine(MakeGetRequest(shapesUrl));
    }

    IEnumerator MakeGetRequest(string uri)
    {
        Console.WriteLine("Making request to: " + uri);
        UnityWebRequest webRequest = useApproov && ApproovService.IsSDKInitialized()
            ? ApproovWebRequest.Get(uri)
            : UnityWebRequest.Get(uri);
        // Add the Api-Key header with the corresponding value
        webRequest.SetRequestHeader("Api-Key", ApiKey);
        // Request and wait for the desired page.
        yield return webRequest.SendWebRequest();

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            string responseBody = webRequest.downloadHandler?.text;
            Debug.LogError("Request failed: " + webRequest.error + " Response: " + responseBody);
            // Set image to error
            shapesImage.sprite = Sprite.Create(images["confused"], new Rect(0, 0, images["confused"].width, images["confused"].height), new Vector2(0.5f, 0.5f));
            // Set text to error: check if return http code is 400
            if (webRequest.responseCode == 400)
            {
                // This probabaly means there is a json error message with `status`, try to parse it
                try
                {
                    ShapesResponse json = JsonUtility.FromJson<ShapesResponse>(responseBody);
                    statusText.text = !string.IsNullOrWhiteSpace(json?.status)
                        ? "Error: " + json.status
                        : "Error: " + responseBody;
                }
                catch (Exception)
                {
                    statusText.text = "Error: " + responseBody;
                }
            }
            else
            {
                statusText.text = "Error: " + (string.IsNullOrWhiteSpace(responseBody) ? webRequest.error : responseBody);
            }
        }
        else
        {
            string responseBody = webRequest.downloadHandler?.text;
            Debug.Log("Received: " + responseBody);
            // This might be a succesfull call to shapes endpoint or hello endpoint
            // The hello endpoint returns a json with key `text` and a message
            ShapesResponse json = JsonUtility.FromJson<ShapesResponse>(responseBody);
            if (json == null || string.IsNullOrWhiteSpace(json.status))
            {
                statusText.text = "Unexpected response: " + responseBody;
                shapesImage.sprite = Sprite.Create(images["confused"], new Rect(0, 0, images["confused"].width, images["confused"].height), new Vector2(0.5f, 0.5f));
                yield break;
            }
            statusText.text = json.status;
            // If the message contains `Hello World!` then set the image to `hello.png`
            if (json.status.Contains("Hello"))
            {
                shapesImage.sprite = Sprite.Create(images["hello"], new Rect(0, 0, images["hello"].width, images["hello"].height), new Vector2(0.5f, 0.5f));
            } else {
            // The shapes endpoint returns a json with key `shape` and a shape name
            string imageName = "confused";
            // Circle, Square, Triangle, Rectangle
                if(json.status.Contains("Circle")) {
                    imageName = "circle";
                } else if(json.status.Contains("Square")) {
                    imageName = "square";
                } else if(json.status.Contains("Triangle")) {
                    imageName = "triangle";
                } else if(json.status.Contains("Rectangle")) {
                    imageName = "rectangle";
                }
                // Set the image to the shape name
                shapesImage.sprite = Sprite.Create(images[imageName], new Rect(0, 0, images[imageName].width, images[imageName].height), new Vector2(0.5f, 0.5f));
            }
        }
    }

    [Serializable]
    private class ShapesResponse
    {
        public string status;
    }
}
