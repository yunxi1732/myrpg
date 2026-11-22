using System;

public static class SaveManagerRuntimeRegistry
{
    private static Func<SaveManager, ISaveManagerRuntime> factory;
    private static Func<SaveManager, bool> suppressInstance;

    public static bool IsRegistered => factory != null;

    public static void Register(Func<SaveManager, ISaveManagerRuntime> create,
        Func<SaveManager, bool> shouldSuppressInstance)
    {
        factory = create ?? throw new ArgumentNullException(nameof(create));
        suppressInstance = shouldSuppressInstance ?? throw new ArgumentNullException(nameof(shouldSuppressInstance));
    }

    public static bool ShouldSuppressInstance(SaveManager owner) => suppressInstance?.Invoke(owner) ?? false;

    public static ISaveManagerRuntime Create(SaveManager owner) =>
        factory != null ? factory(owner) : throw new InvalidOperationException("Save runtime is not registered.");
}
