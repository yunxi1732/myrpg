using System;
using Rpg.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Room setup and menu presentation are owned by the later-loaded menu runtime.
public sealed class MenuRoomFlow
{
    private readonly UI_MainMenu menu;
    private readonly RoomPanel roomPanel;
    private readonly Button onlineButton;
    private readonly UISystem panels = new UISystem();
    private bool[] previousButtonVisibility;
    private bool roomOpen;
    private TcpRoomHost roomHost;
    private TcpRoomClient roomClient;
    private bool ownsBackgroundRun;
    private bool previousBackgroundRun;
    private bool networkSceneView;
    private int appliedTestDelayMs, appliedTestJitterMs, appliedTestLossPercent;
    private bool subscribed;

    public MenuRoomFlow(UI_MainMenu menu)
    {
        this.menu = menu;
        roomPanel = menu.RoomPanel;
        onlineButton = menu.OnlineButton;
    }

    public void Initialize()
    {
        if (roomPanel == null || onlineButton == null) return;
        appliedTestDelayMs = menu.RoomTestSendDelayMs;
        appliedTestJitterMs = menu.RoomTestSendJitterMs;
        appliedTestLossPercent = menu.RoomTestSendLossPercent;
        roomPanel.OnInit();
        panels.RegisterPanel(UIPanelId.Room, roomPanel.gameObject);
        var host = menu.gameObject.AddComponent<ArchitectureHost>();
        roomHost = new TcpRoomHost();
        roomClient = new TcpRoomClient();
        RoomMessageMetrics.Enabled = menu.LogRoomMessageMetrics;
        host.Current.RegistSystem(roomHost);
        host.Current.RegistSystem(roomClient);
        host.Current.InitAllModules();
        var sceneCoordinator = menu.gameObject.AddComponent<RoomSceneCoordinator>();
        sceneCoordinator.Bind(menu, roomHost, roomClient);
        Resume();
    }

    public void Resume()
    {
        if (subscribed || roomHost == null || roomClient == null) return;
        subscribed = true;
        onlineButton.onClick.AddListener(OpenRoomPanel);
        roomPanel.ReturnRequested += CloseRoomPanel;
        roomPanel.CreateRequested += CreateRoom;
        roomPanel.ExitRequested += StopRoom;
        roomPanel.CancelRequested += CancelJoin;
        roomHost.StateChanged += RefreshHostState;
        roomClient.StateChanged += OnClientStateChanged;
        roomHost.Disconnected += OnNetworkDisconnected;
        roomClient.Disconnected += OnNetworkDisconnected;
        roomHost.RoomExited += OnRoomExited;
        roomClient.RoomExited += OnRoomExited;
        RefreshHostState();
    }

    public void Suspend()
    {
        if (subscribed)
        {
            subscribed = false;
            onlineButton.onClick.RemoveListener(OpenRoomPanel);
            roomPanel.ReturnRequested -= CloseRoomPanel;
            roomPanel.CreateRequested -= CreateRoom;
            roomPanel.ExitRequested -= StopRoom;
            roomPanel.CancelRequested -= CancelJoin;
            roomHost.StateChanged -= RefreshHostState;
            roomClient.StateChanged -= OnClientStateChanged;
            roomHost.Disconnected -= OnNetworkDisconnected;
            roomClient.Disconnected -= OnNetworkDisconnected;
            roomHost.RoomExited -= OnRoomExited;
            roomClient.RoomExited -= OnRoomExited;
            roomClient.LeaveRoom();
            roomHost.CloseRoom();
        }
        RefreshBackgroundRun();
        CloseRoomPanel();
    }

    public void Shutdown()
    {
        Suspend();
        panels.Dispose();
        RoomMessageMetrics.Enabled = false;
    }

    public void ShowEntryMessage()
    {
        string entryMessage = RoomSceneCoordinator.TakeEntryMessage();
        if (string.IsNullOrEmpty(entryMessage) || roomPanel == null) return;
        OpenRoomPanel();
        roomPanel.ShowStatus(entryMessage);
    }

    public void OpenRoomPanel()
    {
        if (!menu.ResourcesReady || roomOpen || roomPanel == null || !roomPanel.IsReady ||
            !panels.OpenPanel(UIPanelId.Room)) return;
        roomOpen = true;
        var buttons = menu.MainMenuButtons;
        previousButtonVisibility = new bool[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            previousButtonVisibility[i] = buttons[i].activeSelf;
            buttons[i].SetActive(false);
        }
    }

    public void CloseRoomPanel()
    {
        if (!roomOpen) return;
        if (menu.isActiveAndEnabled && (roomPanel.IsConnectionPending || roomPanel.IsSceneLoading)) return;
        panels.ClosePanel(UIPanelId.Room);
        roomOpen = false;
        if (networkSceneView) return;
        var buttons = menu.MainMenuButtons;
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] != null) buttons[i].SetActive(previousButtonVisibility[i]);
    }

    private void CreateRoom()
    {
        if (roomHost == null || roomHost.IsListening || roomClient == null ||
            roomClient.State == RoomConnectionState.Connecting || roomClient.State == RoomConnectionState.Handshaking ||
            roomClient.Connection != null) return;
        if (!int.TryParse(roomPanel.Port, out int port) || port < 1 || port > 65535)
        {
            roomPanel.ShowStatus("Port must be 1-65535");
            return;
        }
        if (!string.IsNullOrWhiteSpace(roomPanel.Address))
        {
            Debug.Log($"[RoomNetTest] B outgoing delay={menu.RoomTestSendDelayMs}ms jitter={menu.RoomTestSendJitterMs}ms loss={menu.RoomTestSendLossPercent}%");
            roomClient.Connect(roomPanel.Address, port, menu.RoomTestSendDelayMs, menu.RoomTestSendJitterMs, menu.RoomTestSendLossPercent);
            return;
        }
        roomPanel.SetConnectionPending(true);
        string result;
        try
        {
            Debug.Log($"[RoomNetTest] A outgoing delay={menu.RoomTestSendDelayMs}ms jitter={menu.RoomTestSendJitterMs}ms loss={menu.RoomTestSendLossPercent}%");
            roomHost.StartListening(port, menu.RoomTestSendDelayMs, menu.RoomTestSendJitterMs, menu.RoomTestSendLossPercent);
            result = "Room created - waiting for B";
        }
        catch (Exception error) { result = "Cannot create room: " + error.Message; }
        roomPanel.ShowConnectionResult(result);
        RefreshHostState();
    }

    private void RefreshHostState()
    {
        RefreshBackgroundRun();
        if (roomPanel != null && roomHost != null)
        {
            RoomConnectionState connection = roomClient != null ? roomClient.State : RoomConnectionState.Idle;
            roomPanel.SetDisplayState(new RoomPanelDisplayState(
                canCreate: true,
                isHosting: roomHost.IsListening,
                hasGuest: roomHost.GuestIdentity != null,
                isConnected: connection == RoomConnectionState.Connected,
                isConnectionPending: connection == RoomConnectionState.Connecting ||
                                     connection == RoomConnectionState.Handshaking,
                pendingLabel: connection == RoomConnectionState.Handshaking ? "Joining..." : "Connecting..."));
        }
        if (onlineButton != null) onlineButton.interactable = menu.ResourcesReady && roomPanel != null && roomPanel.IsReady;
    }

    private void RefreshBackgroundRun()
    {
        bool required = roomHost != null && roomHost.IsListening || roomClient != null &&
            (roomClient.State == RoomConnectionState.Connecting || roomClient.State == RoomConnectionState.Handshaking ||
             roomClient.State == RoomConnectionState.Connected);
        if (required && !ownsBackgroundRun)
        {
            previousBackgroundRun = Application.runInBackground;
            Application.runInBackground = true;
            ownsBackgroundRun = true;
        }
        else if (!required && ownsBackgroundRun)
        {
            Application.runInBackground = previousBackgroundRun;
            ownsBackgroundRun = false;
        }
    }

    private void OnClientStateChanged()
    {
        if (roomClient.State == RoomConnectionState.Connecting || roomClient.State == RoomConnectionState.Handshaking)
            roomPanel.SetConnectionPending(true);
        RefreshHostState();
        switch (roomClient.State)
        {
            case RoomConnectionState.Connecting: roomPanel.SetConnectionPending(true); break;
            case RoomConnectionState.Handshaking: break;
            case RoomConnectionState.Connected:
                roomPanel.ShowConnectionResult("Room joined");
                Debug.Log($"[RoomIdentity] room={roomClient.RoomSessionId} host=player:{roomClient.HostIdentity.PlayerId}/entity:{roomClient.HostIdentity.EntityId} local=player:{roomClient.LocalIdentity.PlayerId}/entity:{roomClient.LocalIdentity.EntityId}");
                break;
            case RoomConnectionState.Failed:
                roomPanel.ShowConnectionResult(roomClient.FailureMessage);
                Debug.LogWarning("[RoomHandshake] " + roomClient.FailureMessage);
                break;
            case RoomConnectionState.Cancelled: roomPanel.ShowConnectionResult("Connection cancelled"); break;
        }
    }

    private void CancelJoin() => roomClient?.CancelConnect();

    private void OnNetworkDisconnected(NetworkDisconnectedEvent value)
    {
        if (!roomOpen) OpenRoomPanel();
        RefreshHostState();
        roomPanel.ShowStatus("Disconnected: " + value.Reason);
        Debug.LogWarning($"[NetworkDisconnected] room={value.RoomSessionId} connection={value.ConnectionSessionId} reason={value.Reason} {value.Detail}");
    }

    private void StopRoom()
    {
        roomHost?.CloseRoom();
        roomClient?.LeaveRoom();
        RefreshHostState();
    }

    private void OnRoomExited(RoomExitEvent value)
    {
        if (value.Reason == RoomExitReason.HostClosed && !value.IsLocal && !roomOpen) OpenRoomPanel();
        RefreshHostState();
        roomPanel.ShowStatus(value.Reason == RoomExitReason.HostClosed ? "Room closed" :
            value.IsLocal ? "Left room" : "B left - waiting for B");
        Debug.Log($"[RoomExit] room={value.RoomSessionId} connection={value.ConnectionSessionId} reason={value.Reason} local={value.IsLocal}");
    }

    public void ShowSceneProgress(string message, bool loading)
    {
        if (!roomOpen) OpenRoomPanel();
        roomPanel.SetSceneStatus(message, loading);
    }

    public void PreserveRoomInput()
    {
        EventSystem events = EventSystem.current;
        if (events != null && !events.transform.IsChildOf(menu.transform.root))
            events.transform.SetParent(menu.transform.root, false);
    }

    public void ShowNetworkSceneView()
    {
        networkSceneView = true;
        var background = menu.GetComponent<Image>();
        if (background != null) background.enabled = false;
        foreach (Transform child in menu.transform)
        {
            if (child == roomPanel.transform || child.GetComponentInChildren<EventSystem>(true) != null) continue;
            child.gameObject.SetActive(false);
        }
    }

    public void RevealPreparedScene()
    {
        ShowNetworkSceneView();
        CloseRoomPanel();
    }

    public void Tick()
    {
        if (roomPanel == null) return;
        if (appliedTestDelayMs != menu.RoomTestSendDelayMs || appliedTestJitterMs != menu.RoomTestSendJitterMs ||
            appliedTestLossPercent != menu.RoomTestSendLossPercent)
        {
            roomHost?.SetTestNetwork(menu.RoomTestSendDelayMs, menu.RoomTestSendJitterMs, menu.RoomTestSendLossPercent);
            roomClient?.SetTestNetwork(menu.RoomTestSendDelayMs, menu.RoomTestSendJitterMs, menu.RoomTestSendLossPercent);
            appliedTestDelayMs = menu.RoomTestSendDelayMs;
            appliedTestJitterMs = menu.RoomTestSendJitterMs;
            appliedTestLossPercent = menu.RoomTestSendLossPercent;
            Debug.Log($"[RoomNetTest] outgoing updated delay={menu.RoomTestSendDelayMs}ms jitter={menu.RoomTestSendJitterMs}ms loss={menu.RoomTestSendLossPercent}%");
        }
        if (!networkSceneView || roomPanel.IsSceneLoading || !Input.GetKeyDown(KeyCode.Escape)) return;
        if (roomOpen) CloseRoomPanel();
        else OpenRoomPanel();
    }

    public bool BlockSinglePlayerEntry()
    {
        if (roomHost == null || !roomHost.IsListening && (roomClient == null || roomClient.Connection == null)) return false;
        OpenRoomPanel();
        roomPanel.ShowStatus("Exit room before single-player");
        return true;
    }
}
