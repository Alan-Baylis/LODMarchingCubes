﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.CompilerServices; 

using SE;

public class Controller : MonoBehaviour {
	static SE.OpenSimplexNoise noise = new SE.OpenSimplexNoise(3);

	public GameObject MeshPrefab;
	public GameObject ConsoleObject;

	private GameObject CurrentMesh;
	private bool IsRunning = false;

	private static double rconstant = 0.2;
	public int loddebug = 1;
	public int resolution = 16;

	Sample[] sampleFunctions = {
		(float x, float y, float z, float worldSize) => { // Torus
			float r1 = worldSize / 4.0f;
			float r2 = worldSize / 10.0f;
			float q_x = Mathf.Abs(Mathf.Sqrt(x * x + y * y)) - r1;
			float len = Mathf.Sqrt(q_x * q_x + z * z);
			return len - r2;
		},
		(float x, float y, float z, float worldSize) => {
			return (float)(noise.Evaluate(((double)x + 5.5d) * rconstant, ((double)y + 5.5d) * rconstant, ((double)z + 5.5d) * rconstant) * 127d);
		},
		(float x, float y, float z, float worldSize) => {
			return (float)(noise.Evaluate(((double)x + 5.5d) * 0.45, ((double)y + 5.5d) * 0.45, ((double)z + 5.5d) * 0.45) * 127d);
		},

		(float x, float y, float z, float worldSize) => {
			float result = y - 10f;

			result += (float)(noise.Evaluate(((double)x + 5.5d) * 0.1, ((double)y + 5.5d) * 0.1, ((double)z + 5.5d) * 0.1) * 127d) * 0.1f;

			return result;
		},
		(float x, float y, float z, float worldSize) => {
			float result = y - 4f;
			return result;
		}
	};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	float SurfaceD_torus_z(float x, float y, float z, float worldSize) {
		float r1 = worldSize / 4.0f;
		float r2 = worldSize / 10.0f;
		float q_x = Mathf.Abs(Mathf.Sqrt(x * x + y * y)) - r1;
		float len = Mathf.Sqrt(q_x * q_x + z * z);
		return len - r2;

	}

	public delegate float Sample(float x, float y, float z, float worldSize);


	// Use this for initialization
	void Start () {
		IsRunning = true;
		System.Random random = new System.Random(5);
		LookupTableCreator.GenerateLookupTable();
		
		GenerateMesh(3);
		//TestTransvoxel(1);
		ConsoleObject.GetComponent<Console>().SetRegenerateFn(GenerateMesh);
	}
	
	void GenerateMesh(int sampleFn) {
		int res1 = resolution + 1;
		sbyte[][][][] data = new sbyte[res1][][][];

		Sample fn = sampleFunctions[sampleFn];

		float f = 0.01f;
		float nx, ny, nz;
		System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
		System.Diagnostics.Stopwatch sw1 = new System.Diagnostics.Stopwatch();

		sw.Start();
		for(int x = 0; x < res1; x++) {
			data[x] = new sbyte[res1][][];
			for(int y = 0; y < res1; y++) {
				data[x][y] = new sbyte[res1][];
				for(int z = 0; z < res1; z++) {
					data[x][y][z] = new sbyte[4];					

					nx = (float)x - ((float)res1)/2f;
					ny = (float)y - ((float)res1)/2f;
					nz = (float)z - ((float)res1)/2f;

					data[x][y][z][0] = (sbyte)(Mathf.Clamp(-8f * fn(nx, ny, nz, res1), -127, 127));

					float dx = fn(nx+f, ny, nz, (float)res1) - fn(nx-f, ny, nz, (float)res1);
					float dy = fn(nx, ny+f, nz, (float)res1) - fn(nx, ny-f, nz, (float)res1);
					float dz = fn(nx, ny, nz+f, (float)res1) - fn(nx, ny, nz-f, (float)res1);

					float total = (dx*dx) + (dy*dy) + (dz*dz);
					total = Mathf.Sqrt(total);

					dx /= total;
					dy /= total;
					dz /= total;

					dx *= 127;
					dy *= 127;
					dz *= 127;

					data[x][y][z][1] = (sbyte)dx;
					data[x][y][z][2] = (sbyte)dy;
					data[x][y][z][3] = (sbyte)dz;
				} 
			}
		}
		long SampleTime = sw.ElapsedMilliseconds;

		

		//System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
		sw1.Start();
		SE.MCMesh m = SE.MarchingCubes.PolygonizeArea(new Vector3(0, 0, 0), (byte)loddebug, resolution, data);
		sw1.Stop();
		sw.Stop();

		Debug.Log(resolution + "^3 terrain took " + sw1.ElapsedMilliseconds + " ms.");

		Debug.Log(resolution + "^3 terrain took " + sw.ElapsedMilliseconds + " ms.");
		ConsoleObject.GetComponent<Console>().PrintString(resolution + "^3 terrain took " + sw.ElapsedMilliseconds + " ms. (sampling = " + SampleTime + " ms, polyganizing = " + sw1.ElapsedMilliseconds + " ms). " + m.Vertices.Count + " vertices and " + (m.Triangles.Length / 3) + " triangles.");


		Mesh(m);

	}


	void TestTransvoxel(int sF) {
		int resolution = 12;
		int res1 = resolution + 1;

		MCMesh m = new MCMesh();

		List<Vector3> vertices = new List<Vector3>();
		List<int> triangles = new List<int>();

		int sampleFn = 1;
		byte lod = 63;

		UtilFuncs.Sampler fn = (float x, float y, float z) => sampleFunctions[sampleFn](x, y, z, 16);
		SE.Transvoxel.Transvoxel.GenerateChunk(new Vector3(0, 0, 0), vertices, triangles, resolution, fn, lod);

		Debug.Log("0 tris");

		if(triangles.Count > 0) {
			m.Triangles = triangles.ToArray();
			m.Vertices = vertices;
			Mesh(m);
		}
	}

	// Update is called once per frame
	void Update () {
		
	}

	void OnDrawGizmos() {
		if(IsRunning) {
			SE.MarchingCubes.DrawGizmos();
			SE.Transvoxel.Transvoxel.DrawGizmos();
		}
	}

	void Mesh(MCMesh m) {
		Object.Destroy(CurrentMesh);

		GameObject clone = Object.Instantiate(MeshPrefab, new Vector3(0, 0, 0), Quaternion.identity);
		clone.name = "Test mesh";
		

		MeshFilter mf = clone.GetComponent<MeshFilter>();
		UnityEngine.Mesh m2 = new Mesh();
		m2.SetVertices(m.Vertices);
		m2.SetNormals(m.Normals);
		m2.triangles = m.Triangles;
		mf.mesh = m2;
		//m2.RecalculateNormals();

		CurrentMesh = clone;
	}
}
