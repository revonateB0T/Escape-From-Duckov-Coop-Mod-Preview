using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

public class ReviveSystem : MonoBehaviour
{
    public static ReviveSystem Instance { get; private set; }

    public const float MaxBleedingHp = 50f;
    private const float BleedingPerSecond = 1f;
    private const float ReviveRange = 3f;
    private const float BarHeight = 2.5f;
    private const float BarWidth = 1.5f;

    private class BleedingData
    {
        public float BleedingHp;
        public GameObject BarRoot;
        public Image FillImage;
    }

    private readonly Dictionary<string, BleedingData> _bleedingPlayers = new();
    private readonly HashSet<string> _downedPlayers = new();
    private readonly Dictionary<string, GameObject> _promptLabels = new();
    private string _nearbyDownedId;
    private bool _reviveKeyDown;
    private float _bleedingTimer;
    private GameObject _canvas3D;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        CreateWorldCanvas();
    }

    private void CreateWorldCanvas()
    {
        _canvas3D = new GameObject("ReviveWorldCanvas");
        _canvas3D.transform.SetParent(transform);
        var canvas = _canvas3D.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var rect = _canvas3D.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(400, 200);
    }

    private void Update()
    {
        if (!ModBehaviourF.Instance?.networkStarted ?? true) return;

        if (ModBehaviourF.Instance.IsServer)
            ServerTickBleeding(Time.deltaTime);

        UpdateNearbyDetection();
        UpdateBleedingBars();
        UpdatePromptLabels();
        HandleReviveInput();
    }

    private void ServerTickBleeding(float delta)
    {
        _bleedingTimer += delta;
        if (_bleedingTimer < 1f) return;
        _bleedingTimer = 0f;

        var toRemove = new List<string>();
        foreach (var kvp in _bleedingPlayers)
        {
            kvp.Value.BleedingHp -= BleedingPerSecond;
            BroadcastBleedingUpdate(kvp.Key, kvp.Value.BleedingHp);

            if (kvp.Value.BleedingHp <= 0f)
            {
                toRemove.Add(kvp.Key);
                ServerTriggerTrueDeath(kvp.Key);
            }
        }

        foreach (var id in toRemove)
            RemoveBleedingData(id);
    }

    private void BroadcastBleedingUpdate(string playerId, float bleedingHp)
    {
        var rpc = new PlayerDownedStateRpc
        {
            PlayerId = playerId,
            IsDowned = true,
            BleedingHp = bleedingHp
        };
        var writer = new NetDataWriter();
        writer.Put((byte)Op.PLAYER_DOWNED_STATE);
        rpc.Serialize(writer);
        NetService.Instance?.netManager?.SendToAll(writer, DeliveryMethod.ReliableOrdered);
    }

    private void ServerTriggerTrueDeath(string playerId)
    {
        if (NetService.Instance == null) return;

        if (NetService.Instance.clientRemoteCharacters.TryGetValue(playerId, out var go) && go != null)
        {
            var cmc = go.GetComponent<CharacterMainControl>();
            DeadLootBox.Instance?.Server_SpawnPlayerTrueDeathLoot(cmc, playerId);
            GameObject.Destroy(go);
        }

        _downedPlayers.Remove(playerId);
    }

    private void RemoveBleedingData(string playerId)
    {
        if (_bleedingPlayers.TryGetValue(playerId, out var data))
        {
            if (data.BarRoot != null)
                Destroy(data.BarRoot);
            _bleedingPlayers.Remove(playerId);
        }
    }

    private void UpdateNearbyDetection()
    {
        _nearbyDownedId = null;
        if (CharacterMainControl.Main == null) return;

        var localPos = CharacterMainControl.Main.transform.position;
        foreach (var playerId in _downedPlayers)
        {
            if (NetService.Instance?.clientRemoteCharacters.TryGetValue(playerId, out var go) == true && go != null)
            {
                if (Vector3.Distance(localPos, go.transform.position) <= ReviveRange)
                {
                    _nearbyDownedId = playerId;
                    break;
                }
            }
        }
    }

    private void UpdateBleedingBars()
    {
        foreach (var playerId in _downedPlayers)
        {
            BleedingData data;
            if (!_bleedingPlayers.TryGetValue(playerId, out data))
            {
                data = CreateBleedingBar(playerId);
                _bleedingPlayers[playerId] = data;
            }

            if (NetService.Instance?.clientRemoteCharacters.TryGetValue(playerId, out var go) == true && go != null)
            {
                data.BarRoot.SetActive(true);
                data.BarRoot.transform.position = go.transform.position + Vector3.up * BarHeight;
                data.BarRoot.transform.rotation = Quaternion.identity;
                data.FillImage.fillAmount = data.BleedingHp / MaxBleedingHp;
            }
            else
            {
                data.BarRoot.SetActive(false);
            }
        }

        var aliveIds = new HashSet<string>(_downedPlayers);
        var toRemove = new List<string>();
        foreach (var kvp in _bleedingPlayers)
        {
            if (!aliveIds.Contains(kvp.Key))
            {
                if (kvp.Value.BarRoot != null)
                    Destroy(kvp.Value.BarRoot);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var id in toRemove)
            _bleedingPlayers.Remove(id);
    }

    private BleedingData CreateBleedingBar(string playerId)
    {
        var data = new BleedingData { BleedingHp = MaxBleedingHp };

        var root = new GameObject($"BleedingBar_{playerId}");
        root.transform.SetParent(_canvas3D.transform);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 0.005f;
        root.SetActive(false);

        var bgObj = new GameObject("BG");
        bgObj.transform.SetParent(root.transform);
        bgObj.transform.localPosition = Vector3.zero;
        bgObj.transform.localScale = Vector3.one;
        var bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(BarWidth * 100, 12);
        var bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

        var fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(bgObj.transform);
        fillObj.transform.localPosition = new Vector3(-BarWidth * 50, 0, 0);
        fillObj.transform.localScale = Vector3.one;
        var fillRect = fillObj.AddComponent<RectTransform>();
        fillRect.sizeDelta = new Vector2(BarWidth * 100, 12);
        fillRect.anchorMin = new Vector2(0, 0.5f);
        fillRect.anchorMax = new Vector2(0, 0.5f);
        fillRect.pivot = new Vector2(0, 0.5f);
        var fillImg = fillObj.AddComponent<Image>();
        fillImg.color = new Color(1f, 0.85f, 0f, 0.9f);
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 1f;
        data.FillImage = fillImg;

        var outline = bgObj.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2, 2);

        data.BarRoot = root;
        return data;
    }

    private void UpdatePromptLabels()
    {
        var existingIds = new HashSet<string>(_downedPlayers);

        foreach (var playerId in existingIds)
        {
            if (!_promptLabels.TryGetValue(playerId, out var labelGo) || labelGo == null)
            {
                labelGo = CreatePromptLabel(playerId);
                _promptLabels[playerId] = labelGo;
            }

            if (NetService.Instance?.clientRemoteCharacters.TryGetValue(playerId, out var go) == true && go != null)
            {
                labelGo.SetActive(true);
                labelGo.transform.position = go.transform.position + Vector3.up * (BarHeight + 0.3f);
                labelGo.transform.rotation = Quaternion.identity;
                labelGo.GetComponentInChildren<TextMeshProUGUI>().text =
                    playerId == _nearbyDownedId
                        ? "<color=#FFD700>Press F to Revive</color>"
                        : "<color=#FF6666>Downed</color>";
            }
            else
            {
                labelGo.SetActive(false);
            }
        }

        var toRemove = new List<string>();
        foreach (var kvp in _promptLabels)
        {
            if (!existingIds.Contains(kvp.Key))
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var id in toRemove)
            _promptLabels.Remove(id);
    }

    private GameObject CreatePromptLabel(string playerId)
    {
        var label = new GameObject($"Prompt_{playerId}");
        label.transform.SetParent(_canvas3D.transform);
        label.transform.localPosition = Vector3.zero;
        label.transform.localRotation = Quaternion.identity;
        label.transform.localScale = Vector3.one * 0.008f;

        var rect = label.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(400, 60);

        var text = label.AddComponent<TextMeshProUGUI>();
        text.text = "<color=#FF6666>Downed</color>";
        text.fontSize = 48;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.outlineWidth = 0.15f;
        text.outlineColor = Color.black;

        label.SetActive(false);
        return label;
    }

    private void HandleReviveInput()
    {
        if (Input.GetKeyDown(KeyCode.F) && !string.IsNullOrEmpty(_nearbyDownedId) && !_reviveKeyDown)
        {
            _reviveKeyDown = true;
            var rpc = new ReviveRequestRpc { DownedPlayerId = _nearbyDownedId };
            CoopTool.SendRpc(in rpc);
        }
        if (Input.GetKeyUp(KeyCode.F))
            _reviveKeyDown = false;
    }

    public void OnPlayerDownedStateReceived(string playerId, PlayerDownedStateRpc rpc)
    {
        if (rpc.IsDowned)
        {
            _downedPlayers.Add(playerId);

            if (!_bleedingPlayers.TryGetValue(playerId, out var data))
            {
                data = new BleedingData { BleedingHp = rpc.BleedingHp > 0 ? rpc.BleedingHp : MaxBleedingHp };
                _bleedingPlayers[playerId] = data;
            }
            else
            {
                data.BleedingHp = rpc.BleedingHp;
            }
        }
        else
        {
            _downedPlayers.Remove(playerId);
            RemoveBleedingData(playerId);
            RemovePromptLabel(playerId);
        }
    }

    private void RemovePromptLabel(string playerId)
    {
        if (_promptLabels.TryGetValue(playerId, out var go) && go != null)
        {
            Destroy(go);
            _promptLabels.Remove(playerId);
        }
    }

    public void Server_OnReviveRequest(LiteNetLib.NetPeer sender, string downedPlayerId)
    {
        if (!_downedPlayers.Contains(downedPlayerId)) return;

        _downedPlayers.Remove(downedPlayerId);
        RemoveBleedingData(downedPlayerId);
        RemovePromptLabel(downedPlayerId);

        var rpc = new PlayerDownedStateRpc
        {
            PlayerId = downedPlayerId,
            IsDowned = false,
            BleedingHp = MaxBleedingHp
        };
        var writer = new NetDataWriter();
        writer.Put((byte)Op.PLAYER_DOWNED_STATE);
        rpc.Serialize(writer);
        NetService.Instance?.netManager?.SendToAll(writer, DeliveryMethod.ReliableOrdered);
    }

    public void Server_BroadcastDowned(string playerId)
    {
        _downedPlayers.Add(playerId);

        var rpc = new PlayerDownedStateRpc
        {
            PlayerId = playerId,
            IsDowned = true,
            BleedingHp = MaxBleedingHp
        };
        var writer = new NetDataWriter();
        writer.Put((byte)Op.PLAYER_DOWNED_STATE);
        rpc.Serialize(writer);
        NetService.Instance?.netManager?.SendToAll(writer, DeliveryMethod.ReliableOrdered);
    }

    public bool IsDowned(string playerId) => _downedPlayers.Contains(playerId);

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
