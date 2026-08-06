using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TirumalaAR.Map
{
    /// <summary>
    /// Simple offline map viewer: creates a textured quad from StreamingAssets/OfflineMap/map.png or map.tif
    /// and provides pan/zoom/rotate controls. Replace the image with a higher-resolution tile or
    /// implement DEM-based terrain for real 3D elevation.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MapViewer : MonoBehaviour
    {
        [Tooltip("Optional direct Texture2D asset to use for the offline map.")]
        public Texture2D offlineTexture;

        [Tooltip("Path under StreamingAssets where the offline map image is located.")]
        public string offlineImageRelativePath = "OfflineMap/map.png";

        [Tooltip("Supported file extensions when searching for the offline map image.")]
        public string[] supportedOfflineImageExtensions = new[] { "png", "jpg", "jpeg", "tif", "tiff" };

        [Tooltip("Size (meters) of the map quad in world units")]
        public Vector2 mapSize = new Vector2(1000f, 1000f);

        [Tooltip("Minimum and maximum orthographic size for zooming (if using ortho camera)")]
        public Vector2 zoomLimits = new Vector2(10f, 2000f);

        [Tooltip("Camera used to view the map. If null, Camera.main is used.")]
        public Camera mapCamera;

        [Range(0.1f, 10f)] public float zoomSpeed = 1.2f;
        [Range(0.1f, 10f)] public float panSpeed = 1f;

        MeshFilter meshFilter;
        MeshRenderer meshRenderer;

        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            if (mapCamera == null) mapCamera = Camera.main;
            BuildQuad();
            LoadOfflineImage();
        }

        void BuildQuad()
        {
            var mesh = new Mesh();
            var hw = mapSize.x * 0.5f;
            var hh = mapSize.y * 0.5f;
            mesh.vertices = new Vector3[] {
                new Vector3(-hw, 0, -hh),
                new Vector3(hw, 0, -hh),
                new Vector3(-hw, 0, hh),
                new Vector3(hw, 0, hh)
            };
            mesh.uv = new Vector2[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
            mesh.triangles = new int[] { 0,2,1, 2,3,1 };
            mesh.RecalculateNormals();
            meshFilter.mesh = mesh;
            meshRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        void LoadOfflineImage()
        {
            if (offlineTexture != null)
            {
                ApplyTexture(offlineTexture);
                return;
            }

            var relativePath = FindOfflineMapFile();
            if (string.IsNullOrEmpty(relativePath))
            {
                Debug.LogWarning($"Offline map image not found in StreamingAssets/OfflineMap. Place a PNG, JPG, or TIFF at StreamingAssets/{offlineImageRelativePath}");
                return;
            }

            if (relativePath.EndsWith(".tif", System.StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tiff", System.StringComparison.OrdinalIgnoreCase))
            {
#if UNITY_EDITOR
                var converted = ConvertTiffToPng(relativePath);
                if (!string.IsNullOrEmpty(converted))
                {
                    relativePath = converted;
                }
                else
                {
                    Debug.LogWarning($"Failed to convert TIFF offline map '{relativePath}'. Assign the imported Texture2D to OfflineTexture or use a PNG.");
                    return;
                }
#else
                Debug.LogWarning($"TIFF offline map '{relativePath}' is not supported at runtime. Assign a Texture2D asset to OfflineTexture or use a PNG/JPG in StreamingAssets.");
                return;
#endif
            }

            var path = Path.Combine(Application.streamingAssetsPath, relativePath);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"Offline map image not found at {path}. Place a tile at StreamingAssets/{relativePath}");
                return;
            }

            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Debug.LogWarning("Failed to load offline map image into texture.");
                return;
            }

            ApplyTexture(tex);
        }

        void ApplyTexture(Texture2D texture)
        {
            if (texture == null)
                return;

            texture.wrapMode = TextureWrapMode.Clamp;
            meshRenderer.material.mainTexture = texture;
        }

        string FindOfflineMapFile()
        {
            var basePath = Path.ChangeExtension(offlineImageRelativePath, null);
            foreach (var extension in supportedOfflineImageExtensions)
            {
                var candidate = basePath + "." + extension;
                var fullPath = Path.Combine(Application.streamingAssetsPath, candidate);
                if (File.Exists(fullPath))
                {
                    return candidate.Replace('\\', '/');
                }
            }

            return null;
        }

#if UNITY_EDITOR
        string ConvertTiffToPng(string tiffRelativePath)
        {
            var assetPath = Path.Combine("Assets/StreamingAssets", tiffRelativePath.Replace('/', Path.DirectorySeparatorChar));
            assetPath = assetPath.Replace('\\', '/');
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (source == null)
                return null;

            var pngData = source.EncodeToPNG();
            if (pngData == null || pngData.Length == 0)
                return null;

            var pngRelativePath = Path.ChangeExtension(tiffRelativePath, ".png");
            var pngFullPath = Path.Combine(Application.streamingAssetsPath, pngRelativePath);
            File.WriteAllBytes(pngFullPath, pngData);
            AssetDatabase.ImportAsset(Path.Combine("Assets/StreamingAssets", pngRelativePath).Replace('\\', '/'));
            AssetDatabase.Refresh();
            return pngRelativePath.Replace('\\', '/');
        }
#endif

        void Update()
        {
            HandlePanZoom();
        }

        void HandlePanZoom()
        {
            if (mapCamera == null) return;

            // Zoom with mouse wheel or touch pinch
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                if (mapCamera.orthographic)
                {
                    var size = mapCamera.orthographicSize;
                    size /= Mathf.Pow(zoomSpeed, scroll * 10f);
                    mapCamera.orthographicSize = Mathf.Clamp(size, zoomLimits.x, zoomLimits.y);
                }
                else
                {
                    mapCamera.transform.position += mapCamera.transform.forward * scroll * 200f * Time.deltaTime;
                }
            }

            // Pan with middle mouse button or right mouse
            if (Input.GetMouseButton(2) || Input.GetMouseButton(1))
            {
                var dx = -Input.GetAxisRaw("Mouse X") * panSpeed;
                var dz = -Input.GetAxisRaw("Mouse Y") * panSpeed;
                var right = mapCamera.transform.right;
                var forward = Vector3.ProjectOnPlane(mapCamera.transform.forward, Vector3.up).normalized;
                mapCamera.transform.position += (right * dx + forward * dz);
            }

            // Simple touch: one finger pan, two-finger pinch zoom (not robust)
            if (Input.touchCount == 1)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Moved)
                {
                    var dx = -t.deltaPosition.x * panSpeed * 0.01f;
                    var dz = -t.deltaPosition.y * panSpeed * 0.01f;
                    var right = mapCamera.transform.right;
                    var forward = Vector3.ProjectOnPlane(mapCamera.transform.forward, Vector3.up).normalized;
                    mapCamera.transform.position += (right * dx + forward * dz);
                }
            }
            else if (Input.touchCount == 2)
            {
                var t0 = Input.GetTouch(0);
                var t1 = Input.GetTouch(1);
                var prevDist = (t0.position - t0.deltaPosition - (t1.position - t1.deltaPosition)).magnitude;
                var curDist = (t0.position - t1.position).magnitude;
                var diff = curDist - prevDist;
                if (mapCamera.orthographic)
                {
                    var size = mapCamera.orthographicSize - diff * 0.01f * zoomSpeed;
                    mapCamera.orthographicSize = Mathf.Clamp(size, zoomLimits.x, zoomLimits.y);
                }
                else
                {
                    mapCamera.transform.position += mapCamera.transform.forward * diff * 0.1f * Time.deltaTime;
                }
            }
        }

        // Public API for UI buttons
        public void ZoomIn()
        {
            if (mapCamera == null) return;
            if (mapCamera.orthographic) mapCamera.orthographicSize = Mathf.Max(zoomLimits.x, mapCamera.orthographicSize / zoomSpeed);
            else mapCamera.transform.position += mapCamera.transform.forward * 10f;
        }

        public void ZoomOut()
        {
            if (mapCamera == null) return;
            if (mapCamera.orthographic) mapCamera.orthographicSize = Mathf.Min(zoomLimits.y, mapCamera.orthographicSize * zoomSpeed);
            else mapCamera.transform.position -= mapCamera.transform.forward * 10f;
        }
    }
}
