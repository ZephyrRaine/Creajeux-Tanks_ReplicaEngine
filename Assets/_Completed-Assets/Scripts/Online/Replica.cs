using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Reflection;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
[CustomEditor(typeof(Replica))]
public class ReplicaEditor : Editor
{
    Dictionary<System.Type, bool> ReplicaComponents = new Dictionary<System.Type, bool>();
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        Replica replica = target as Replica;
        foreach(Component comp in replica.GetComponents<Component>())
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(comp.GetType().ToString());
            bool replicate = replica.m_ComponentsToReplicate.ContainsKey(comp.GetType());
            if(replicate != EditorGUILayout.Toggle(replicate))
            {
                if(replicate)
                    replica.m_ComponentsToReplicate.Remove(comp.GetType());
                else
                    replica.m_ComponentsToReplicate[comp.GetType()] = new List<String>();
            }
            bool disable = replica.m_ComponentsToDisable.Contains(comp.GetType());
            if (disable != EditorGUILayout.Toggle(disable))
            {
                if(disable)
                    replica.m_ComponentsToDisable.Remove(comp.GetType());
                else
                    replica.m_ComponentsToDisable.Add(comp.GetType());
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Foldout(replicate, "");
            if(replicate)
            {
                EditorGUILayout.BeginVertical();
                foreach (PropertyInfo prop in comp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    string lol = prop.DeclaringType.ToString();

                    Type myType = prop.PropertyType;
                    if((!myType.IsClass || myType.ToString() == ("System.String")) && prop.CanWrite && prop.CanRead && myType.Name != "enabled" && myType.ToString() != "UnityEngine.HideFlags") 
                    {
                        bool replicateVar = replica.m_ComponentsToReplicate[comp.GetType()].Contains(prop.Name);
                        if(replicateVar != EditorGUILayout.Toggle(prop.Name + " (" + myType.ToString() + ")", replicateVar))
                        {
                            if(replicateVar)
                                replica.m_ComponentsToReplicate[comp.GetType()].Remove(prop.Name);
                            else
                                replica.m_ComponentsToReplicate[comp.GetType()].Add(prop.Name);
                        }
                    }
                }
                EditorGUILayout.EndVertical();    
            }
            EditorGUILayout.EndHorizontal();
            EditorUtility.SetDirty(replica.gameObject);
        }
    }
}

#endif

    public enum ReplicaConditionFlag
    {
        Always,
        OnChange, //need specific function
    }


public class ReplicaComponent
{
    public enum Type
    {
        Transform = 0,
        TankHealth,
        TankShooting,
        GameNetworkManager,
        END
    }

    public static Type ComponentToReplicaComponentType(Component _comp)
    {
        if (_comp is Transform)
            return Type.Transform;
        if (_comp is Complete.TankHealth)
            return Type.TankHealth;
        if (_comp is Complete.TankShooting)
            return Type.TankShooting;
        if (_comp is GameNetworkManager)
            return Type.GameNetworkManager;
        return Type.END;
    }
}



public class Replica : MonoBehaviour , ISerializationCallbackReceiver{

    static uint KeyGen = 0;

    [HideInInspector]
    public uint m_UID = 0;

    //<editable>
    public bool OnlyOnHost = false;
    public bool OnlyForLocalPlayer = true;
    public ReplicaConditionFlag m_condition = ReplicaConditionFlag.Always;
    public Dictionary<System.Type, List<String>> m_ComponentsToReplicate = new Dictionary<System.Type, List<String>>();

    public List<System.Type> m_ComponentsToDisable = new List<System.Type>();
    //</editable>

    public ReplicaCondition m_replicaCondition;
    public int m_PlayerID = 0;
    public Dictionary<System.Type, Component> m_components = new Dictionary<System.Type, Component>();

	void Start () {
        m_UID = KeyGen++;
        foreach (var type in m_ComponentsToReplicate)
        {
            var comp = GetComponent(type.Key);
            Debug.Assert(comp);
            m_components.Add(type.Key, comp);
        }
        m_replicaCondition = ReplicaCondition.CreateCondition(m_condition, this);
        GameNetworkManager.Singleton.RegisterReplica(this);
        if (!GameNetworkManager.Singleton.IsLocalPlayer(m_PlayerID))
        {
            foreach (var type in m_ComponentsToDisable)
            {
                var comp = GetComponent(type) as Behaviour;
                Debug.Assert(comp);
                comp.enabled = false;
            }
        }
	}

    private void Update()
    {
        if (!GameNetworkManager.Singleton.IsLocalPlayer(m_PlayerID))
        {
            foreach (var type in m_ComponentsToDisable)
            {
                var comp = GetComponent(type) as Behaviour;
                Debug.Assert(comp);
                comp.enabled = false;
            }
        }
        
    }

    public static Type GetType(string TypeName)
    {

        // Try Type.GetType() first. This will work with types defined
        // by the Mono runtime, etc.
        var type = Type.GetType(TypeName);

        // If it worked, then we're done here
        if (type != null)
            return type;

        // Get the name of the assembly (Assumption is that we are using
        // fully-qualified type names)
        var assemblyName = TypeName.Substring(0, TypeName.IndexOf('.'));

        // Attempt to load the indicated Assembly
        var assembly = Assembly.LoadWithPartialName(assemblyName);
        if (assembly == null)
            return null;

        // Ask that assembly to return the proper Type
        return assembly.GetType(TypeName);
    }

    //<serialization>
    [SerializeField, HideInInspector]
    List<string> ser_ComponentsToReplicate = new List<string>(); //for serialization purpose
    [SerializeField, HideInInspector]
    List<string> ser_ComponentsToDisable = new List<string>(); //for serialization purpose
    public void OnBeforeSerialize()
    {
        ser_ComponentsToReplicate.Clear();
        foreach(var comp in m_ComponentsToReplicate)
        {
            ser_ComponentsToReplicate.Add(comp.Key.ToString());
            ser_ComponentsToReplicate.AddRange(comp.Value);
        }

        ser_ComponentsToDisable.Clear();
        foreach (var comp in m_ComponentsToDisable)
        {
            ser_ComponentsToDisable.Add(comp.ToString());
        }
    }

    public void OnAfterDeserialize()
    {
        m_ComponentsToReplicate.Clear();
        Type type;
        foreach(var str in ser_ComponentsToReplicate)
        {
            type = GetType(str);
            if(type != null)
                m_ComponentsToReplicate[type] = new List<string>();
            else
                m_ComponentsToReplicate[type].Add(str);

        }
       m_ComponentsToDisable.Clear();
        foreach(var str in ser_ComponentsToDisable)
        {
            Type typeDis = GetType(str);
            if(typeDis != null)
                m_ComponentsToDisable.Add(typeDis);
        }
    }
    //</serialization>



}


