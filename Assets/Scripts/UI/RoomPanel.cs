using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Rpg.Networking;

public sealed class RoomPanel : BasePanel
{
    [SerializeField] private TMP_InputField ipInput;
    [SerializeField] private TMP_InputField portInput;
    [SerializeField] private Button enterRoomButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button exitRoomButton;
    [SerializeField] private Button returnButton;
    private float statusRemaining;
    private bool creationAvailable;
    private bool hosting;
    private bool hasGuest;
    private RoomConnectionState clientState;
    private string sceneStatus;
    public bool IsSceneLoading { get; private set; }

    public event Action ReturnRequested;
    public event Action CreateRequested;
    public event Action ExitRequested;
    public event Action CancelRequested;
    public bool IsReady { get; private set; }
    public bool IsConnectionPending { get; private set; }
    public string Address => ipInput.text.Trim();
    public string Port => portInput.text.Trim();

    public override void OnInit()
    {
        IsReady = ipInput != null && portInput != null && enterRoomButton != null &&
            statusText != null && exitRoomButton != null && returnButton != null;
        if (!IsReady) Debug.LogError("[RoomPanel] Assign all room panel controls before opening.", this);
        if (IsReady)
        {
            portInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            portInput.characterLimit = 5;
            if (ipInput.placeholder is TMP_Text placeholder) placeholder.text = "Join IP (blank = create)";
            ResetPresentation();
        }
        base.OnInit();
    }

    public override void OnShow(params object[] args)
    {
        if (!IsReady) return;
        ResetPresentation();
        base.OnShow(args);
    }

    public override void OnClose()
    {
        if (IsReady) ResetPresentation();
        ClearSelection();
        base.OnClose();
    }

    private void ResetPresentation()
    {
        IsConnectionPending = false;
        RefreshControls();
        HideStatus();
    }

    public void SetHostState(bool canCreate, bool listening, bool guestConnected,
        RoomConnectionState connectionState = RoomConnectionState.Idle)
    {
        creationAvailable = canCreate;
        hosting = listening;
        hasGuest = guestConnected;
        clientState = connectionState;
        IsConnectionPending = clientState == RoomConnectionState.Connecting || clientState == RoomConnectionState.Handshaking;
        if (IsReady) RefreshControls();
    }

    private void RefreshControls()
    {
        bool connected = clientState == RoomConnectionState.Connected;
        ipInput.interactable = portInput.interactable = !hosting && !connected && !IsConnectionPending;
        enterRoomButton.interactable = creationAvailable && !hosting && !connected && !IsConnectionPending;
        exitRoomButton.interactable = (hosting || connected) && !IsConnectionPending;
        returnButton.interactable = !IsConnectionPending && !IsSceneLoading;
        var label = enterRoomButton.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = !string.IsNullOrEmpty(sceneStatus) ? sceneStatus : IsConnectionPending ? (clientState == RoomConnectionState.Handshaking ? "Joining..." : "Connecting...") :
            hosting ? (hasGuest ? "B joined" : "Waiting for B") : connected ? "Room joined" :
            string.IsNullOrWhiteSpace(Address) ? "Create Room" : "Join Room";
    }

    public void SetSceneStatus(string message, bool loading)
    {
        sceneStatus = message;
        IsSceneLoading = loading;
        if (IsReady) RefreshControls();
    }

    // UI state only. The connection owner sets this before starting and after success/failure.
    public void SetConnectionPending(bool pending)
    {
        if (!IsReady || !isActiveAndEnabled || IsConnectionPending == pending) return;
        if (pending)
        {
            HideStatus();
            ClearSelection();
        }
        IsConnectionPending = pending;
        RefreshControls();
    }

    public void ShowConnectionResult(string message)
    {
        SetConnectionPending(false);
        ShowStatus(message);
    }

    // Called by the future create/join result handler, after the outcome is known.
    public void ShowStatus(string message)
    {
        if (!IsReady || !isActiveAndEnabled) return;
        HideStatus();
        if (string.IsNullOrWhiteSpace(message)) return;
        statusText.text = message;
        statusText.gameObject.SetActive(true);
        statusRemaining = 1f;
    }

    private void Update()
    {
        if (statusRemaining <= 0f) return;
        statusRemaining -= Time.unscaledDeltaTime;
        if (statusRemaining <= 0f) HideStatus();
    }

    private void HideStatus()
    {
        statusRemaining = 0f;
        if (statusText == null) return;
        statusText.text = string.Empty;
        statusText.gameObject.SetActive(false);
    }

    [ContextMenu("Preview Status Message (Play Mode)")]
    private void PreviewStatus()
    {
        if (Application.isPlaying) ShowStatus("Status preview");
    }

    [ContextMenu("Preview Connection Waiting (Play Mode)")]
    private void PreviewWaiting()
    {
        if (Application.isPlaying) SetConnectionPending(true);
    }

    [ContextMenu("Preview Connection Finished (Play Mode)")]
    private void PreviewFinished()
    {
        if (Application.isPlaying) ShowConnectionResult("Status preview");
    }

    private void OnEnable()
    {
        if (returnButton != null) returnButton.onClick.AddListener(RequestReturn);
        if (enterRoomButton != null) enterRoomButton.onClick.AddListener(RequestCreate);
        if (exitRoomButton != null) exitRoomButton.onClick.AddListener(RequestExit);
        if (ipInput != null) ipInput.onValueChanged.AddListener(OnAddressChanged);
    }

    private void OnDisable()
    {
        if (returnButton != null) returnButton.onClick.RemoveListener(RequestReturn);
        if (enterRoomButton != null) enterRoomButton.onClick.RemoveListener(RequestCreate);
        if (exitRoomButton != null) exitRoomButton.onClick.RemoveListener(RequestExit);
        if (ipInput != null) ipInput.onValueChanged.RemoveListener(OnAddressChanged);
        HideStatus();
        ClearSelection();
    }

    private void RequestReturn()
    {
        if (!IsConnectionPending && !IsSceneLoading) ReturnRequested?.Invoke();
    }

    private void RequestCreate()
    {
        if (!IsConnectionPending && creationAvailable && !hosting) CreateRequested?.Invoke();
    }

    private void RequestExit()
    {
        if (!IsConnectionPending && (hosting || clientState == RoomConnectionState.Connected)) ExitRequested?.Invoke();
    }

    private void OnAddressChanged(string value) { if (IsReady) RefreshControls(); }

    [ContextMenu("Cancel Connection Attempt (Play Mode)")]
    private void CancelConnectionAttempt()
    {
        if (Application.isPlaying && IsConnectionPending) CancelRequested?.Invoke();
    }

    private void ClearSelection()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(transform))
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnDestroy()
    {
        ReturnRequested = CreateRequested = ExitRequested = CancelRequested = null;
    }
}
