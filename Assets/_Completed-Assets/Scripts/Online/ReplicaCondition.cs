using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReplicaCondition
{
    //factory
    public static ReplicaCondition CreateCondition(ReplicaConditionFlag _flag, Replica _rep)
    {
        switch (_flag)
        {
            case ReplicaConditionFlag.OnChange:
                    return new ReplicaHasChangeCondition(_rep);
            default:
                break;

        }
        return null;
    }

    public ReplicaCondition(Replica _rep) { }
    public virtual bool CheckCondition(Replica _rep) { return false; }
    public virtual void AfterSerialize(Replica _rep) { }
}

public class ReplicaHasChangeCondition : ReplicaCondition
{
    public class ComponentChangeChecker
    {
        public virtual bool HasChanged() { return false; }
        public virtual void Reset() {}
    }
    private List<ComponentChangeChecker> m_checkers = new List<ComponentChangeChecker>();

    public ReplicaHasChangeCondition(Replica _rep) : base(_rep)
    {
        foreach(var comp in _rep.m_components)
        {
            switch(comp.Key)
            {
                case ReplicaComponent.Type.GameNetworkManager:
                    m_checkers.Add(new GameNetworkManager.GameNetworkManagerComponentChangeChecker(comp.Value as GameNetworkManager));
                    break;
            }

        }
    }

    public override bool CheckCondition(Replica _rep)
    {
        foreach(var checker in m_checkers)
        {
            if (checker.HasChanged())
                return true;
        }
        return false;
    }

    public override void AfterSerialize(Replica _rep)
    {
        foreach (var checker in m_checkers)
        {
            checker.Reset();
        }
    }
}

