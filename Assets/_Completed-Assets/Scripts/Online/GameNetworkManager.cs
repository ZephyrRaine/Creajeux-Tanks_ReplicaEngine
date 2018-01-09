using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using UnityEngine.SceneManagement;

public class GameNetworkManager : NetworkManager
{

    /// <PROTOCOL>
    const short REPLICA_MSG = 0x1000;

    //note : we should register exact same messages for every clients since we want to simulate a p2p
    void RegisterProtocol(NetworkConnection _conn)
    {
        _conn.RegisterHandler(REPLICA_MSG, OnReplicaMsg);
    }
    /// </PROTOCOL>

    /// <P2P>

    private bool m_isHost = false;
    public bool m_Connected = false;

    private int upload = 0;
    private int download = 0;
    private float bandwithUpdateDelay = 0;

    //Send message to all : abstract client/server distinction from basic UNet module
    public void SendToAll(short _msgType, MessageBase _msg)
    {
        if (!IsClientConnected())
            return;
        if (m_isHost)
        {
            NetworkServer.SendToAll(_msgType, _msg);
        }
        else
        {
            client.Send(_msgType, _msg);
        }
        NetworkWriter writer = new NetworkWriter();
        _msg.Serialize(writer);
        upload += writer.Position;
    }
    /// </P2P>
    static new GameNetworkManager singleton;
    public static GameNetworkManager Singleton
    {
        get
        {
            if (!singleton)
            {
                singleton = new GameNetworkManager();
            }
            return singleton;
        }
    }

    void Awake()
    {
        if (!singleton)
        {
            singleton = this;
        }
    }

    public override void OnServerConnect(NetworkConnection conn)
    {
        base.OnServerConnect(conn);
        //for each client we register handler server to this client connection
        RegisterProtocol(conn);
        m_isHost = true;
        m_Connected = true;
        RegisterNewPlayer();
    }

    public override void OnClientConnect(NetworkConnection conn)
    {
        base.OnClientConnect(conn);
        //register handlers for client to server connection 
        //except for host to avoid callback on our own message
        if (!m_isHost)
        {
            RegisterProtocol(conn);
        }
        m_Connected = true;
    }


    private struct PlayerParam
    {
        public int m_ID;
        public Color m_color;
        public string m_name;
    }

    private List<PlayerParam> m_players = new List<PlayerParam>();
    void RegisterNewPlayer()
    {
        PlayerParam newPlayer;
        newPlayer.m_ID = m_players.Count;
        newPlayer.m_color = Color.red;
        newPlayer.m_name = "Player" + newPlayer.m_ID.ToString();
        m_players.Add(newPlayer);
    }

    enum State
    {
        Lobby,
        Loading,
        InGame
    }
    private State m_currentState = State.Lobby;
    private void AskForState(State _state)
    {
        if (m_currentState == _state)
            return;
        switch (_state)
        {
            case State.InGame:
                {
                    m_currentState = State.Loading;
                    break;
                }
            default:
                {
                    m_currentState = _state;
                    break;
                }
        }
    }
    public GameObject m_tankPrefab;
    private void OnGUI()
    {
        switch (m_currentState)
        {
            case State.Lobby:
                {
                    int playerCount = 0;
                    foreach (PlayerParam player in m_players)
                    {
                        GUI.Label(new Rect(Screen.width / 2, Screen.height / 2 + playerCount * 20, 120, 20), player.m_name);
                        playerCount++;

                    }
                    if (playerCount >= 2)
                    {
                        if (IsHostPlayer())
                        {
                            if (GUI.Button(new Rect(Screen.width / 2, Screen.height / 2 + playerCount * 20, 120, 20), "START"))
                            {
                                m_currentState++;
                            }
                        }
                    }
                    break;
                }
            case State.Loading:
                {
                    SceneManager.LoadScene("CompleteMainScene");
                    m_currentState++;
                    break;
                }
        }

    }
    void OnEnable()
    {
        //Tell our 'OnLevelFinishedLoading' function to start listening for a scene change as soon as this script is enabled.
        SceneManager.sceneLoaded += OnLevelFinishedLoading;
    }

    void OnDisable()
    {
        //Tell our 'OnLevelFinishedLoading' function to stop listening for a scene change as soon as this script is disabled. Remember to always have an unsubscription for every delegate you subscribe to!
        SceneManager.sceneLoaded -= OnLevelFinishedLoading;
    }

    void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode)
    {
        Debug.Log("Level Loaded");
        Debug.Log(scene.name);
        Debug.Log(mode);
        Complete.GameManager gm = null;
        var gobjs = scene.GetRootGameObjects();
        foreach (var gobj in gobjs)
        {
            if (gobj.name == "GameManager")
                gm = gobj.GetComponent<Complete.GameManager>();

        }
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("Respawn");
        if (gm != null)
        {
            gm.m_Tanks = new Complete.TankManager[m_players.Count];
            int playerCount = 0;
            foreach (PlayerParam player in m_players)
            {
                gm.m_Tanks[playerCount] = new Complete.TankManager();
                gm.m_Tanks[playerCount].m_PlayerColor = player.m_color;
                gm.m_Tanks[playerCount].m_PlayerNumber = player.m_ID;
                gm.m_Tanks[playerCount].m_SpawnPoint = spawnPoints[playerCount].transform;
                playerCount++;
            }
        }
    }



    void SpawnPlayer()
    {
        //spawn player
        //set its id
        //disable some components
    }

    void SpawnFoe()
    {

    }

    /// <GAMEPLAY>
    static uint KeyGen = 0;
    uint GenerateUID()
    {
        return KeyGen++;
    }


    //factory


    Dictionary<uint, Replica> m_Replica = new Dictionary<uint, Replica>();
    public void RegisterReplica(Replica _rep)
    {
        Debug.Assert(_rep != null);
        if (!m_Replica.ContainsKey(_rep.m_UID))
        {
            m_Replica.Add(_rep.m_UID, _rep);
        }
    }

    //return true if character linked to its id is handle locally
    public bool IsLocalPlayer(int _player)
    {
        //since we have 2 players only here, use simple trick :
        // 1 is host, 2 is client
        if (_player == 0 && m_isHost)
            return true;
        if (_player == 1 && !m_isHost)
            return true;
        return false;
    }

    //return true if character linked to its id is handle locally
    public bool IsHostPlayer()
    {
        if (!IsClientConnected())
            return true;
        return m_isHost;
    }

    private float m_delay = 0.0f;
    public float m_tick = 0.1f;
    public float upBytesPerSecond = 0.0f;
    public float downBytesPerSecond = 0.0f;

    void OnReplicaMsg(NetworkMessage _msg)
    {
        var msg = _msg.ReadMessage<ReplicaMsg>();
        NetworkReader reader = new NetworkReader(msg.Buffer);
        Deserialize(reader);
    }

    class ReplicaMsg : MessageBase
    {
        public byte[] Buffer = null;
    }


    /// </GAMEPLAY>

    void Update()
    {
        if (m_Connected)
        {
            bandwithUpdateDelay += Time.deltaTime;
            if (bandwithUpdateDelay > 1)
            {
                upBytesPerSecond = upload / bandwithUpdateDelay;
                upload = 0;
                downBytesPerSecond = download / bandwithUpdateDelay;
                download = 0;
                bandwithUpdateDelay = 0;
            }

            m_delay += Time.deltaTime;

            if (m_delay < m_tick)
                return;

            m_delay = 0;

            //Send message
            NetworkWriter writer = new NetworkWriter();
            Serialize(writer);

            var msg = new ReplicaMsg();
            msg.Buffer = writer.ToArray();
            SendToAll(REPLICA_MSG, msg);
        }
    }

    void Serialize(NetworkWriter _writer)
    {
        foreach (var rep in m_Replica)
        {
            if (rep.Value.OnlyOnHost && !IsHostPlayer())
                continue;
            if (rep.Value.OnlyForLocalPlayer && !IsLocalPlayer(rep.Value.m_PlayerID))
                continue;
            if (rep.Value.m_replicaCondition != null && !rep.Value.m_replicaCondition.CheckCondition(rep.Value))
                continue;
            _writer.Write(rep.Key);
            foreach (var comp in rep.Value.m_components)
            {
                _writer.Write(comp.Key.ToString());
                foreach(var variable in rep.Value.m_ComponentsToReplicate[comp.Key])
                {
                    _writer.Write(variable);
                }

                /*switch (comp.Key)
                {
                    case ReplicaComponent.Type.Transform:
                        Serialize(_writer, comp.Value as Transform);
                        break;
                    case ReplicaComponent.Type.TankHealth:
                        Serialize(_writer, comp.Value as Complete.TankHealth);
                        break;
                    case ReplicaComponent.Type.TankShooting:
                        Serialize(_writer, comp.Value as Complete.TankShooting);
                        break;
                    case ReplicaComponent.Type.GameNetworkManager:
                        Serialize(_writer, comp.Value as GameNetworkManager);
                        break;
                    default:
                        Debug.Assert(false, "Serialization not implemented for " + rep.GetType());
                        break;
                }*/
            }
            _writer.Write((int)ReplicaComponent.Type.END);
            if (rep.Value.m_replicaCondition != null)
                rep.Value.m_replicaCondition.AfterSerialize(rep.Value);
        }
    }
    void Deserialize(NetworkReader _reader)
    {
        while (_reader.Position != _reader.Length)
        {
            uint UID = _reader.ReadUInt32();
            var replica = m_Replica[UID];
            ReplicaComponent.Type componentType = (ReplicaComponent.Type)_reader.ReadInt32();
            while (componentType != ReplicaComponent.Type.END)
            {
                var comp = replica.m_components[componentType];
                switch (componentType)
                {
                    case ReplicaComponent.Type.Transform:
                        Deserialize(_reader, comp as Transform);
                        break;
                    case ReplicaComponent.Type.TankHealth:
                        Deserialize(_reader, comp as Complete.TankHealth);
                        break;
                    case ReplicaComponent.Type.TankShooting:
                        Deserialize(_reader, comp as Complete.TankShooting);
                        break;
                    case ReplicaComponent.Type.GameNetworkManager:
                        Deserialize(_reader, comp as GameNetworkManager);
                        break;
                    default:
                        Debug.Assert(false, "Deserialization not found for " + componentType);
                        break;
                }
                componentType = (ReplicaComponent.Type)_reader.ReadInt32();
            }
        }
    }

    //SPECIALISATION DE LA SERIALIZATION DES COMPOSANTS
    ///<gameNetworkManager>
    void Serialize(NetworkWriter _writer, GameNetworkManager net)
    {
        _writer.Write((int)net.m_currentState);
        _writer.Write(net.m_players.Count);
        for (int i = 0; i < net.m_players.Count; ++i)
        {
            _writer.Write(net.m_players[i].m_ID);
            _writer.Write(net.m_players[i].m_name);
            _writer.Write(net.m_players[i].m_color);

        }
    }
    void Deserialize(NetworkReader _reader, GameNetworkManager net)
    {
        State currentState = (State)_reader.ReadInt32();
        net.AskForState(currentState);
        int playerCount = _reader.ReadInt32();
        net.m_players = new List<PlayerParam>();
        for (int i = 0; i < playerCount; i++)
        {
            var player = new PlayerParam();
            player.m_ID = _reader.ReadInt32();
            player.m_name = _reader.ReadString();
            player.m_color = _reader.ReadColor();
            net.m_players.Add(player);
        }
    }
    ///</gamenetworkManager>
    ///<transform>
    void Serialize(NetworkWriter _writer, Transform transform)
    {
        _writer.Write(transform.position);
        _writer.Write(transform.rotation);
    }
    void Deserialize(NetworkReader _reader, Transform transform)
    {
        transform.position = _reader.ReadVector3();
        transform.rotation = _reader.ReadQuaternion();
    }
    ///</transform>
    ///<TankHealth>
    void HasChanged(Complete.TankHealth health)
    {

    }
    void Serialize(NetworkWriter _writer, Complete.TankHealth health)
    {
        _writer.Write(health.m_CurrentHealth);
    }
    void Deserialize(NetworkReader _reader, Complete.TankHealth health)
    {
        health.m_CurrentHealth = _reader.ReadSingle();
    }
    ///</TankHealt>
    ///<TankShooting>
    void Serialize(NetworkWriter _writer, Complete.TankShooting shot)
    {
        _writer.Write(shot.m_LastLaunchForce);
        _writer.Write(shot.m_CurrentLaunchForce);
        _writer.Write(shot.m_Fired);
        if (shot.m_Fired)
        {
            _writer.Write(shot.m_FireTransform.position);
            _writer.Write(shot.m_FireTransform.rotation);
        }
    }
    void Deserialize(NetworkReader _reader, Complete.TankShooting shot)
    {
        float lastLaunchForce = _reader.ReadSingle();
        shot.m_CurrentLaunchForce = _reader.ReadSingle();
        if (_reader.ReadBoolean() && lastLaunchForce != shot.m_LastLaunchForce)
        {
            shot.m_FireTransform.position = _reader.ReadVector3();
            shot.m_FireTransform.rotation = _reader.ReadQuaternion();
            shot.m_CurrentLaunchForce = lastLaunchForce;
            shot.Fire();
            shot.m_Fired = false;
        }
    }
    ///</TankHealt>


    public class GameNetworkManagerComponentChangeChecker : ReplicaHasChangeCondition.ComponentChangeChecker
    {
        GameNetworkManager m_net;
        private GameNetworkManager.State m_currentState = 0;
        private int m_playerCount = 0;
        public GameNetworkManagerComponentChangeChecker(GameNetworkManager _net)
        {
            m_net = _net;
        }
        public override bool HasChanged()
        {
            if (m_net.m_currentState != m_currentState)
                return true;
            if (m_net.m_players.Count != m_playerCount)
                return true;
            return false;
        }
        public override void Reset()
        {
            m_currentState = m_net.m_currentState;
            m_playerCount = m_net.m_players.Count;
        }
    }
}
