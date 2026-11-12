using System;
using Rpg.Networking;
using Rpg.Networking.Protocol;

public sealed partial class PlayerCombatContext
{
    private SkillTreeStateDto roomSkillTree;
    private ulong skillTreeTick;
    private long skillUnlockRequestSeq;
    public long SkillTreeVersion => roomSkillTree?.version ?? 1;
    public int RoomCurrency => roomSkillTree?.currency ?? 0;
    internal SkillTreeStateDto GrantRoomMonsterCurrency(int amount, ulong tick)
    {
        if (!Owner.IsAuthoritative || roomSkillTree == null || amount < 0) throw new InvalidOperationException("Only A grants room monster currency.");
        roomSkillTree.currency = checked(roomSkillTree.currency + amount);
        roomSkillTree.version = checked(roomSkillTree.version + 1);
        skillTreeTick = tick;
        return CaptureRoomSkillTree();
    }
    public bool IsRoomNodeUnlocked(string id) => FindNode(roomSkillTree, id)?.unlocked == true;
    public int RoomNodeCost(string id) => FindNode(roomSkillTree, id)?.cost ?? -1;
    private static SkillTreeNodeDto FindNode(SkillTreeStateDto value, string id)
    { if (value != null) foreach (var node in value.nodes) if (node.nodeId == id) return node; return null; }
    private static SkillTreeStateDto CopyTree(SkillTreeStateDto value)
    {
        var copy = new SkillTreeStateDto { version = value.version, currency = value.currency,
            unlockedSkills = (bool[])value.unlockedSkills.Clone(), nodes = new SkillTreeNodeDto[value.nodes.Length] };
        for (int i = 0; i < copy.nodes.Length; i++) copy.nodes[i] = new SkillTreeNodeDto {
            nodeId = value.nodes[i].nodeId, cost = value.nodes[i].cost, unlocked = value.nodes[i].unlocked };
        return copy;
    }
    public SkillTreeStateDto CaptureRoomSkillTree() => roomSkillTree == null ? new SkillTreeStateDto() : CopyTree(roomSkillTree);
    private void ProjectRoomSkillUnlocks()
    {
        if (roomSkillTree == null) return;
        for (int i = 0; i < 7; i++) Skills.Get((RoomSkillId)i).SetRoomUnlocked(roomSkillTree.unlockedSkills[i]);
        if (Skills.Get<DodgeSkill>().Unlocked && !roomDodgeApplied)
        {
            Stats.evasion.AddModifier(roomDodgeBonus);
            roomDodgeApplied = true;
        }
    }
    private void RestoreRoomSkillTree(SkillTreeStateDto value)
    {
        if (!IsNewerTree(value)) return;
        roomSkillTree = CopyTree(value);
    }
    private bool IsNewerTree(SkillTreeStateDto value) => roomSkillTree == null || value.version > roomSkillTree.version ||
        value.version == roomSkillTree.version && roomSkillTree.nodes.Length == 0 && value.nodes.Length > 0;
    internal void InitializeRoomSkillTree(RoomSkillTreeCatalog catalog, int currency)
    {
        if (!Owner.IsAuthoritative || RoomConfigurationId == null || catalog == null || currency < 0) throw new InvalidOperationException("Use A's room skill catalog and currency.");
        if (roomSkillTree != null && roomSkillTree.nodes.Length > 0) return;
        roomSkillTree = catalog.InitialState(this, currency); ProjectRoomSkillUnlocks();
    }
    internal bool ApplyRoomSkillTreeSnapshot(SkillTreeStateDto value, ulong tick)
    {
        if (EntityRuntimePermissions.Identity(Owner)?.Role != NetworkEntityRole.Display || tick == 0 || !ProtocolCodec.ValidSkillTree(value) ||
            tick < skillTreeTick || !IsNewerTree(value)) return false;
        roomSkillTree = CopyTree(value); skillTreeTick = tick; ProjectRoomSkillUnlocks(); return true;
    }
    internal SkillUnlockResultDto UnlockRoomSkill(SkillUnlockRequestDto request, RoomSkillTreeCatalog catalog, ulong tick, AuthorityResultSequence results)
    {
        if (!ProtocolCodec.ValidSkillUnlockRequest(request) || request.roomSessionId != RoomSessionId ||
            request.requestSeq <= skillUnlockRequestSeq) return null;
        skillUnlockRequestSeq = request.requestSeq;
        var rejection = SkillUnlockRejection.None;
        RoomSkillTreeCatalog.Node rule = null;
        var selected = FindNode(roomSkillTree, request.nodeId);
        if (!Owner.IsRuntimeInitialized || !Stats.IsRuntimeInitialized || Stats.isDead || Stats.currentHealth <= 0) rejection = SkillUnlockRejection.PlayerUnavailable;
        else if (request.expectedVersion != SkillTreeVersion) rejection = SkillUnlockRejection.StaleState;
        else if (!catalog.TryGet(request.nodeId, out rule) || selected == null) rejection = SkillUnlockRejection.UnknownNode;
        else if (selected.unlocked) rejection = SkillUnlockRejection.AlreadyUnlocked;
        else
        {
            foreach (var required in rule.Required) if (!IsRoomNodeUnlocked(required)) { rejection = SkillUnlockRejection.Prerequisite; break; }
            if (rejection == SkillUnlockRejection.None)
                foreach (var excluded in rule.Excluded) if (IsRoomNodeUnlocked(excluded)) { rejection = SkillUnlockRejection.Excluded; break; }
            if (rejection == SkillUnlockRejection.None && RoomCurrency < rule.Cost) rejection = SkillUnlockRejection.InsufficientCurrency;
        }
        if (rejection == SkillUnlockRejection.None)
        {
            long nextVersion = checked(SkillTreeVersion + 1);
            roomSkillTree.currency -= rule.Cost; selected.unlocked = true; roomSkillTree.version = nextVersion; skillTreeTick = tick;
            if (RoomSkillTreeCatalog.BaseSkill(request.nodeId, out var skill)) roomSkillTree.unlockedSkills[(int)skill] = true;
            ProjectRoomSkillUnlocks();
        }
        return new SkillUnlockResultDto { roomSessionId = RoomSessionId, authorityTick = tick, resultId = results.Allocate(),
            playerId = PlayerId, entityId = EntityId, requestSeq = request.requestSeq, nodeId = request.nodeId,
            accepted = rejection == SkillUnlockRejection.None, rejection = rejection, state = CaptureRoomSkillTree() };
    }
}
