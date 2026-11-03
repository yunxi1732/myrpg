public interface IArchitecture : System.IDisposable
{
    void RegistModel<T>(T instance) where T : class, IModel;
    void RegistSystem<T>(T instance) where T : class, ISystem;
    void RegistUtility<T>(T instance) where T : class, IUtility;
    T GetModel<T>() where T : class, IModel;
    T GetSystem<T>() where T : class, ISystem;
    T GetUtility<T>() where T : class, IUtility;
    void InitAllModules();
    void RegistEvent<T>(System.Action<object> onEvent);
    void UnRegistEvent<T>(System.Action<object> onEvent);
    void SendEvent<T>(object data);
}
