/// <summary>Explicit panel lifecycle used by the panel owner.</summary>
public interface IBasePanel
{
    void OnInit();
    void OnShow(params object[] args);
    void OnClose();
}
