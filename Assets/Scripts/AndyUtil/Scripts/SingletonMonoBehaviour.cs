using Sirenix.OdinInspector;
using UnityEngine;

public abstract class SingletonMonoBehaviour<T> : SerializedMonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<T>();
            }

            return _instance;
        }
    }

    protected abstract void OnSingletonInitialized();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {

            Destroy(this.gameObject);
            return;
        }

        _instance = this as T;
        OnSingletonInitialized();
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }
}

public abstract class PersistentSingletonMonoBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<T>();

            }

            return _instance;
        }
        set => _instance = value;
    }

    protected abstract void OnSingletonInitialized();



    private void Awake()
    {
        if (_instance != null && _instance != this)
        {

            Destroy(this.gameObject);
            return;
        }

        _instance = this as T;
        DontDestroyOnLoad(_instance.gameObject);
        OnSingletonInitialized();
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }
}