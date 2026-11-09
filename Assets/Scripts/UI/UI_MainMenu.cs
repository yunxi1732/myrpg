using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System;
using System.Net.Sockets;
using Rpg.Networking;
using UnityEngine.EventSystems;

public class UI_MainMenu : MonoBehaviour
{
    [SerializeField] private string sceneName = "MainScene";
    [SerializeField] private GameObject continueButton;
    [SerializeField] UI_FadeScreen fadeScreen;
    [SerializeField] private RoomPanel roomPanel;
    [SerializeField] private Button onlineButton;
    [SerializeField] private GameObject[] mainMenuButtons = new GameObject[0];
    private readonly UISystem panels = new UISystem();
    private bool[] previousButtonVisibility;
    private bool roomOpen;
    private TcpRoomHost roomHost;
    private TcpRoomClient roomClient;
    private bool ownsBackgroundRun;
    private bool previousBackgroundRun;
    private RoomSceneCoordinator sceneCoordinator;
    private bool networkSceneView;

    private void Awake()
    {
        if (roomPanel == null || onlineButton == null) return;
        roomPanel.OnInit();
        onlineButton.interactable = roomPanel.IsReady;
        panels.RegisterPanel(UIPanelId.Room, roomPanel.gameObject);
        var host = gameObject.AddComponent<ArchitectureHost>();
        roomHost = new TcpRoomHost();
        roomClient = new TcpRoomClient();
        host.Current.RegistSystem(roomHost);
        host.Current.RegistSystem(roomClient);
        host.Current.InitAllModules();
        sceneCoordinator = gameObject.AddComponent<RoomSceneCoordinator>();
        sceneCoordinator.Bind(this, roomHost, roomClient);
        RefreshHostState();
    }

    private void OnEnable()
    {
        if (onlineButton != null) onlineButton.onClick.AddListener(OpenRoomPanel);
        if (roomPanel != null) roomPanel.ReturnRequested += CloseRoomPanel;
        if (roomPanel != null) roomPanel.CreateRequested += CreateRoom;
        if (roomPanel != null) roomPanel.ExitRequested += StopRoom;
        if (roomHost != null) roomHost.StateChanged += RefreshHostState;
        if (roomClient != null) roomClient.StateChanged += OnClientStateChanged;
        if (roomPanel != null) roomPanel.CancelRequested += CancelJoin;
        if (roomHost != null) roomHost.Disconnected += OnNetworkDisconnected;
        if (roomClient != null) roomClient.Disconnected += OnNetworkDisconnected;
        if (roomHost != null) roomHost.RoomExited += OnRoomExited;
        if (roomClient != null) roomClient.RoomExited += OnRoomExited;
        RefreshHostState();
    }

    private void OnDisable()
    {
        if (onlineButton != null) onlineButton.onClick.RemoveListener(OpenRoomPanel);
        if (roomPanel != null) roomPanel.ReturnRequested -= CloseRoomPanel;
        if (roomPanel != null) roomPanel.CreateRequested -= CreateRoom;
        if (roomPanel != null) roomPanel.ExitRequested -= StopRoom;
        if (roomHost != null) roomHost.StateChanged -= RefreshHostState;
        if (roomClient != null) roomClient.StateChanged -= OnClientStateChanged;
        if (roomPanel != null) roomPanel.CancelRequested -= CancelJoin;
        if (roomHost != null) roomHost.Disconnected -= OnNetworkDisconnected;
        if (roomClient != null) roomClient.Disconnected -= OnNetworkDisconnected;
        if (roomHost != null) roomHost.RoomExited -= OnRoomExited;
        if (roomClient != null) roomClient.RoomExited -= OnRoomExited;
        roomClient?.LeaveRoom();
        roomHost?.CloseRoom();
        RefreshBackgroundRun();
        CloseRoomPanel();
    }

    private void OnDestroy()
    {
        if (ownsBackgroundRun) Application.runInBackground = previousBackgroundRun;
        ownsBackgroundRun = false;
        panels.Dispose();
    }

    public void OpenRoomPanel()
    {
        if (roomOpen || roomPanel == null || !roomPanel.IsReady || !panels.OpenPanel(UIPanelId.Room)) return;
        roomOpen = true;
        previousButtonVisibility = new bool[mainMenuButtons.Length];
        for (int i = 0; i < mainMenuButtons.Length; i++)
        {
            if (mainMenuButtons[i] == null) continue;
            previousButtonVisibility[i] = mainMenuButtons[i].activeSelf;
            mainMenuButtons[i].SetActive(false);
        }
    }

    public void CloseRoomPanel()
    {
        if (!roomOpen) return;
        if (isActiveAndEnabled && (roomPanel.IsConnectionPending || roomPanel.IsSceneLoading)) return;
        // Return only hides the panel. Exit Room (or owner teardown) closes the listener.
        panels.ClosePanel(UIPanelId.Room);
        roomOpen = false;
        if (networkSceneView) return; // Do not bring the original main-menu buttons back over MainScene.
        for (int i = 0; i < mainMenuButtons.Length; i++)
            if (mainMenuButtons[i] != null) mainMenuButtons[i].SetActive(previousButtonVisibility[i]);
    }

    private void CreateRoom()
    {
        if (roomHost == null || roomHost.IsListening || roomClient == null ||
            roomClient.State == RoomConnectionState.Connecting || roomClient.State == RoomConnectionState.Handshaking || roomClient.Connection != null) return;
        if (!int.TryParse(roomPanel.Port, out int port) || port < 1 || port > 65535)
        {
            roomPanel.ShowStatus("Port must be 1-65535");
            return;
        }
        // An empty address explicitly selects local hosting; connection failure never creates a room.
        if (!string.IsNullOrWhiteSpace(roomPanel.Address))
        {
            roomClient.Connect(roomPanel.Address, port);
            return;
        }
        roomPanel.SetConnectionPending(true);
        string result;
        try
        {
            roomHost.StartListening(port);
            result = "Room created - waiting for B";
        }
        catch (SocketException error)
        {
            result = error.SocketErrorCode == SocketError.AddressAlreadyInUse ? "Port is already in use" : "Cannot listen: " + error.SocketErrorCode;
        }
        catch (Exception error) { result = "Cannot create room: " + error.Message; }
        roomPanel.ShowConnectionResult(result);
        RefreshHostState();
    }

    private void RefreshHostState()
    {
        RefreshBackgroundRun();
        if (roomPanel != null && roomHost != null)
            roomPanel.SetHostState(true, roomHost.IsListening, roomHost.GuestIdentity != null,
                roomClient != null ? roomClient.State : RoomConnectionState.Idle);
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
        if (events != null && !events.transform.IsChildOf(transform.root))
            events.transform.SetParent(transform.root, false);
    }

    public void ShowNetworkSceneView()
    {
        networkSceneView = true;
        var background = GetComponent<Image>();
        if (background != null) background.enabled = false;
        // Keep the existing room panel and its input system; hide the original menu decorations/fade overlay.
        foreach (Transform child in transform)
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

    private void Update()
    {
        if (!networkSceneView || roomPanel.IsSceneLoading || !Input.GetKeyDown(KeyCode.Escape)) return;
        if (roomOpen) CloseRoomPanel();
        else OpenRoomPanel();
    }

    private bool BlockSinglePlayerEntry()
    {
        if (roomHost == null || !roomHost.IsListening && (roomClient == null || roomClient.Connection == null)) return false;
        OpenRoomPanel();
        roomPanel.ShowStatus("Exit room before single-player");
        return true;
    }

    private void Start()
    {
        if (SaveManager.instance.HaveSavedData() == false)
            continueButton.SetActive(false);
        string entryMessage = RoomSceneCoordinator.TakeEntryMessage();
        if (!string.IsNullOrEmpty(entryMessage))
        {
            OpenRoomPanel();
            roomPanel.ShowStatus(entryMessage);
        }
    }

    public void ContinueGame()
    {
        if (BlockSinglePlayerEntry()) return;
        AudioManager.instance.PlaySFX(4, null);
        StartCoroutine(LoadSceneWithFadeEffect(1.5f));
    }

    public void NewGame()
    {
        if (BlockSinglePlayerEntry()) return;
        AudioManager.instance.PlaySFX(4, null);
        SaveManager.instance.DeleteSavedData();
        StartCoroutine(LoadSceneWithFadeEffect(1.5f));
    }

    public void ExitGame()
    {
        AudioManager.instance.PlaySFX(4, null);
        Debug.Log("Exit game!");
    }

    IEnumerator LoadSceneWithFadeEffect(float _delay)
    {
        fadeScreen.FadeOut();

        yield return GameSimulationClock.Wait(_delay);

        SceneManager.LoadScene(sceneName);
    }
}
