/**
 * Copyright (c) 2019 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using System.Collections.Generic;
using UnityEngine;
using Simulator.Map;

public class NPCManager : MonoBehaviour
{
    public LayerMask NPCSpawnCheckBitmask;
    private float checkRadius = 6f;
    private Camera activeCamera;

    public bool isDespawnTimer = false;
    public bool isRightSideDriving = true;
    public bool isSpawnAreaVisible = false;
    public bool isSpawnAreaLimited = true;
    public bool isSimplePhysics = true;
    public Vector3 spawnArea = Vector3.zero;
    public float despawnDistance = 300f;
    private Bounds spawnBounds = new Bounds();
    private Color spawnColor = Color.magenta;
    private Vector3 spawnPos;
    private Transform spawnT;
    public List<GameObject> npcVehicles = new List<GameObject>();
    int k = 0;

    private bool _npcActive = false;
    public bool NPCActive
    {
        get => _npcActive;
        set => _npcActive = value;
    }

    public enum NPCCountType
    {
        Low = 150,
        Medium = 125,
        High = 50
    };
    public NPCCountType npcCountType = NPCCountType.Low;

    private int npcCount = 0;
    private int activeNPCCount = 0;
    [HideInInspector]
    public List<GameObject> currentPooledNPCs = new List<GameObject>();
    private System.Random RandomGenerator;
    
    private void Awake()
    {
        if (spawnT == null)
            spawnT = transform;

        if (activeCamera == null)
            activeCamera = Camera.main;
    }

    public void InitRandomGenerator(int seed) => RandomGenerator = new System.Random(seed);

    private void Start()
    {
        NPCSpawnCheckBitmask = 1 << LayerMask.NameToLayer("NPC") | 1 << LayerMask.NameToLayer("Agent");
        npcCount = Mathf.CeilToInt(SimulatorManager.Instance.MapManager.totalLaneDist / (int)npcCountType);
        SpawnNPCPool();
        Debug.Log("b run =======================================");
    }

    void FixedUpdate()
    {
        for (int i = 0; i < currentPooledNPCs.Count; i++)
        {
            var npc = currentPooledNPCs[i];
            var npcController = npc.GetComponent<NPCController>();
            if (npc.activeInHierarchy)
            {
                print("FixedUpdate (" + k + " " + i + "): " + npc.name.Substring(0, npc.name.IndexOf("(")) + " " + npc.transform.position + " " + npcController.currentSpeed);
                npcController.PhysicsUpdate();
            }
        }

        if (NPCActive)
        {
            if (activeNPCCount < npcCount)
                SetNPCOnMap();
        }
        else
        {
            DespawnAllNPC();
        }
        k++;
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    }

    public void DespawnVehicle(NPCController obj)
    {
        obj.StopNPCCoroutines();
        obj.currentIntersection?.npcsInIntersection.Remove(obj.transform);
        Destroy(obj.gameObject);
    }

    public GameObject SpawnVehicle(string name, Vector3 position, Quaternion rotation)
    {
        var template = npcVehicles.Find(obj => obj.name == name);
        if (template == null)
        {
            return null;
        }

        var genId = System.Guid.NewGuid().ToString();
        var go = new GameObject("NPC " + genId);
        go.transform.SetParent(transform);
        go.layer = LayerMask.NameToLayer("NPC");
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 2000;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        go.AddComponent<NPCController>();
        go.name = Instantiate(template, go.transform).name + genId;
        var NPCController = go.GetComponent<NPCController>();
        NPCController.id = genId;
        NPCController.Init(RandomGenerator.Next());

        SimulatorManager.Instance.UpdateSemanticTags(go);

        go.transform.SetPositionAndRotation(position, rotation);
        return go;
    }

    #region npc
    private void SpawnNPCPool()
    {
        for (int i = 0; i < currentPooledNPCs.Count; i++)
        {
            Destroy(currentPooledNPCs[i]);
        }
        currentPooledNPCs.Clear();
        activeNPCCount = 0;

        int poolCount = Mathf.FloorToInt(npcCount + (npcCount * 0.1f));
        for (int i = 0; i < poolCount; i++)
        {
            var genId = System.Guid.NewGuid().ToString();
            var go = new GameObject("NPC " + genId);
            go.transform.SetParent(transform);
            go.layer = LayerMask.NameToLayer("NPC");
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 2000;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            go.AddComponent<NPCController>();
            go.name = Instantiate(npcVehicles[RandomIndex(npcVehicles.Count)], go.transform).name + genId;
            var NPCController = go.GetComponent<NPCController>();
            NPCController.id = genId;
            NPCController.Init(RandomGenerator.Next());
            currentPooledNPCs.Add(go);
            go.SetActive(false);

            SimulatorManager.Instance.UpdateSemanticTags(go);
        }
    }

    private void SetNPCOnMap()
    {
        for (int i = 0; i < currentPooledNPCs.Count; i++)
        {
            if (currentPooledNPCs[i].activeInHierarchy)
            {
                continue;
            }
            var mapManager = SimulatorManager.Instance.MapManager;
            var lane = mapManager.GetLane(RandomGenerator.Next(mapManager.trafficLanes.Count));
            if (lane == null) return;

            if (lane.mapWorldPositions == null || lane.mapWorldPositions.Count == 0)
                continue;

            if (lane.mapWorldPositions.Count < 2)
                continue;

            if (!lane.Spawnable)
                continue;

            var start = lane.mapWorldPositions[0];

            if (isSpawnAreaLimited)
            {
                if (IsPositionWithinSpawnArea(start))
                {
                    if (!Physics.CheckSphere(lane.mapWorldPositions[0], checkRadius, NPCSpawnCheckBitmask))
                    {
                        spawnPos = lane.mapWorldPositions[0];
                        currentPooledNPCs[i].transform.position = spawnPos;
                        if (!IsVisible(currentPooledNPCs[i]))
                        {
                            currentPooledNPCs[i].GetComponent<NPCController>().InitLaneData(lane);
                            currentPooledNPCs[i].SetActive(true);
                            currentPooledNPCs[i].transform.LookAt(lane.mapWorldPositions[1]); // TODO check if index 1 is valid
                            activeNPCCount++;
                        }
                        else
                        {
                            currentPooledNPCs[i].transform.position = transform.position;
                            currentPooledNPCs[i].transform.rotation = Quaternion.identity;
                            currentPooledNPCs[i].SetActive(false);
                        }
                    }
                }
            }
            else
            {
                if (!Physics.CheckSphere(lane.mapWorldPositions[0], checkRadius, NPCSpawnCheckBitmask))
                {
                    spawnPos = lane.mapWorldPositions[0];
                    currentPooledNPCs[i].transform.position = spawnPos;
                    currentPooledNPCs[i].GetComponent<NPCController>().InitLaneData(lane);
                    currentPooledNPCs[i].SetActive(true);
                    currentPooledNPCs[i].transform.LookAt(lane.mapWorldPositions[1]); // TODO check if index 1 is valid
                    activeNPCCount++;
                    print("Spawn " + currentPooledNPCs[i].name.Substring(0, currentPooledNPCs[i].name.IndexOf("(")) + " " + spawnPos);
                }
            }
        }
    }

    public Transform GetRandomActiveNPC()
    {
        if (currentPooledNPCs.Count == 0) return transform;

        int index = RandomGenerator.Next(0, currentPooledNPCs.Count);
        while (!currentPooledNPCs[index].activeInHierarchy)
        {
            index = RandomGenerator.Next(0, currentPooledNPCs.Count);
        }
        return currentPooledNPCs[index].transform;
    }

    public void DespawnNPC(GameObject npc)
    {
        activeNPCCount--;
        npc.SetActive(false);
        npc.transform.position = transform.position;
        npc.transform.rotation = Quaternion.identity;
    }

    public void DespawnAllNPC()
    {
        if (activeNPCCount == 0) return;
        StopAllCoroutines();

        for (int i = 0; i < currentPooledNPCs.Count; i++)
        {
            DespawnNPC(currentPooledNPCs[i]);
            foreach (var item in FindObjectsOfType<MapIntersection>())
                item.stopQueue.Clear();
        }
        activeNPCCount = 0;
    }
    
    public void ToggleNPCPhysicsMode(bool state)
    {
        isSimplePhysics = !state;
    }
    #endregion

    #region utilities
    private int RandomIndex(int max = 1)
    {
        return RandomGenerator.Next(0, max);
    }

    public bool IsPositionWithinSpawnArea(Vector3 pos)
    {
        Transform tempT = SimulatorManager.Instance.AgentManager.CurrentActiveAgent?.transform;
        if (tempT != null)
            spawnT = tempT;

        spawnBounds = new Bounds(spawnT.position, spawnArea);
        if (spawnBounds.Contains(pos))
            return true;
        else
            return false;
    }

    public bool IsVisible(GameObject npc)
    {
        Camera tempCam = Camera.main;
        if (tempCam != null)
            activeCamera = tempCam;
        var npcColliderBounds = npc.GetComponent<Collider>().bounds;
        var activeCameraPlanes = GeometryUtility.CalculateFrustumPlanes(activeCamera);
        return GeometryUtility.TestPlanesAABB(activeCameraPlanes, npcColliderBounds);
    }

    private void DrawSpawnArea()
    {
        Transform tempT = SimulatorManager.Instance.AgentManager.CurrentActiveAgent?.transform;
        if (tempT != null)
            spawnT = tempT;
        Gizmos.matrix = spawnT.localToWorldMatrix;
        Gizmos.color = spawnColor;
        Gizmos.DrawWireCube(Vector3.zero, spawnArea);
    }

    private void OnDrawGizmosSelected()
    {
        if (!isSpawnAreaVisible) return;
        DrawSpawnArea();
    }
    #endregion
}
