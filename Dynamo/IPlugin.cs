namespace Dynamo {
    public interface IPlugin {
        string Name { get; }
        string Version { get; }
        void Run();
    }
}