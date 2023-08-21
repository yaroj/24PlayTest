
using UnityEngine;

public class TaskResult
{
	public GeneratedMesh leftMesh;
	public GeneratedMesh rightMesh;
	public Vector3 position;
	public Quaternion rotation;
	public Vector3 scale;
	public Material[] materials;
	public MeshFilter originalMeshFilter;
	public MeshRenderer originalMeshRenderer;
	public GameObject originalObject;
	public TaskResult(GeneratedMesh leftMesh,
		GeneratedMesh rightMesh,
		Vector3 position,
		Quaternion rotation,
		Vector3 scale,
		Material[] materials
		, MeshFilter originalMeshFilter
		, MeshRenderer originalMeshRenderer
		, GameObject originalObject
		)
	{
		this.leftMesh = leftMesh;
		this.rightMesh = rightMesh;
		this.position = position;
		this.rotation = rotation;
		this.scale = scale;
		this.materials = materials;
		this.originalMeshFilter = originalMeshFilter;
		this.originalMeshRenderer = originalMeshRenderer;
		this.originalObject = originalObject;
	}
}
