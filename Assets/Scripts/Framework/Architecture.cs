using System;
using System.Collections.Generic;

/// <summary>Register all modules, then explicitly initialize utilities, models and systems.</summary>
public class Architecture : IArchitecture
{
    private readonly IOCContainer container = new IOCContainer();
    private readonly GameEventSystem events = new GameEventSystem();
    private readonly HashSet<Type> registeredTypes = new HashSet<Type>();
    private readonly List<INeedInit> utilities = new List<INeedInit>();
    private readonly List<INeedInit> models = new List<INeedInit>();
    private readonly List<INeedInit> systems = new List<INeedInit>();
    private readonly List<INeedInit> initialized = new List<INeedInit>();
    private bool started;
    private bool completed;
    private bool disposed;
    private bool initializing;

    public void RegistModel<T>(T instance) where T : class, IModel => Register(instance, models);
    public void RegistSystem<T>(T instance) where T : class, ISystem => Register(instance, systems);
    public void RegistUtility<T>(T instance) where T : class, IUtility => Register(instance, utilities);
    public T GetModel<T>() where T : class, IModel => container.Get<T>();
    public T GetSystem<T>() where T : class, ISystem => container.Get<T>();
    public T GetUtility<T>() where T : class, IUtility => container.Get<T>();
    public void RegistEvent<T>(Action<object> onEvent) => events.Regist<T>(onEvent);
    public void UnRegistEvent<T>(Action<object> onEvent) => events.UnRegist<T>(onEvent);
    public void SendEvent<T>(object data) => events.Send<T>(data);

    private void Register<T>(T instance, List<INeedInit> modules) where T : class, INeedInit
    {
        if (disposed || started)
            throw new InvalidOperationException("Modules must be registered before initialization.");
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (!registeredTypes.Add(typeof(T)))
            throw new InvalidOperationException("Module type already registered: " + typeof(T));
        container.Register<T>(instance);
        modules.Add(instance);
    }

    public void InitAllModules()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(Architecture));
        if (completed)
            return;
        if (started)
            throw new InvalidOperationException("Initialization is in progress or previously failed. Create a new architecture after failure.");
        started = true;
        initializing = true;
        try
        {
            Initialize(utilities);
            Initialize(models);
            Initialize(systems);
            completed = true;
        }
        finally { initializing = false; }
    }

    public void Update()
    {
        if (!completed || disposed) return;
        // initialized contains each object once, including modules registered through multiple interfaces.
        for (int i = 0; i < initialized.Count && !disposed; i++)
            (initialized[i] as IUpdateModule)?.Update();
    }

    /// <summary>Dispose participating modules in reverse initialization order, then drop all references.</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        if (initializing)
            throw new InvalidOperationException("Cannot dispose during initialization.");
        disposed = true;
        events.Dispose();
        var errors = new List<Exception>();
        for (int i = initialized.Count - 1; i >= 0; i--)
        {
            try { (initialized[i] as IDisposable)?.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        initialized.Clear();
        utilities.Clear();
        models.Clear();
        systems.Clear();
        registeredTypes.Clear();
        container.Clear();
        if (errors.Count > 0)
            throw new AggregateException("Module cleanup failed.", errors);
    }

    private void Initialize(List<INeedInit> modules)
    {
        foreach (INeedInit module in modules)
        {
            bool alreadyInitialized = false;
            foreach (INeedInit previous in initialized)
            {
                if (ReferenceEquals(previous, module))
                {
                    alreadyInitialized = true;
                    break;
                }
            }
            if (alreadyInitialized)
                continue;
            initialized.Add(module);
            module.Init();
        }
    }
}
