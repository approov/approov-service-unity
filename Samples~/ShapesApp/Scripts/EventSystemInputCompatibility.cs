using System;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class EventSystemInputCompatibility : MonoBehaviour
{
#if ENABLE_INPUT_SYSTEM
    private const string InputSystemUiModuleTypeName =
        "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem";
#endif

    private void Awake()
    {
        ConfigureEventSystem();
    }

    private void ConfigureEventSystem()
    {
        StandaloneInputModule standaloneInputModule = GetComponent<StandaloneInputModule>();

#if ENABLE_INPUT_SYSTEM
        Type inputSystemModuleType = Type.GetType(InputSystemUiModuleTypeName);
        if (inputSystemModuleType == null)
        {
            return;
        }

        if (standaloneInputModule != null)
        {
            standaloneInputModule.enabled = false;
        }

        BaseInputModule inputSystemModule = GetComponent(inputSystemModuleType) as BaseInputModule;
        if (inputSystemModule == null)
        {
            inputSystemModule = gameObject.AddComponent(inputSystemModuleType) as BaseInputModule;
        }

        if (inputSystemModule != null)
        {
            inputSystemModule.enabled = true;
        }
#else
        if (standaloneInputModule != null)
        {
            standaloneInputModule.enabled = true;
        }
#endif
    }
}
