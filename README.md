# Tirumala AR Navigation — Alipiri Mettu

Offline outdoor AR walking guidance for the Alipiri steps to Tirumala.
Unity 6 (6000.5) · URP · AR Foundation 6.5 · ARCore.

---

## First run

1. Open the project. Unity will resolve the newly added AR packages on first import
   (this needs internet **once**; the built app is fully offline).
2. Run **Tools ▸ Tirumala AR ▸ Set Up Everything**.
   This generates the arrow prefab, the landmark marker, `AppConfig.asset`,
   both scenes, and the Android build settings.
3. Open `Assets/Scenes/MainMenu.unity`.
4. Build to an ARCore-capable Android device (AR does not run in the editor).

---

## Where each script lives

| Folder | Script | System |
|---|---|---|
| `Scripts/Core` | `ServiceLocator`, `EventBus`, `ObjectPool`, `StateMachine` | 20 |
| `Scripts/Data` | `GeoCoordinate`, `Waypoint`, `LandmarkData`, `NavigationEvents` | — |
| `Scripts/Utilities` | `GeoMath`, `GpsKalmanFilter`, `SignalFilters`, `PolylineUtility`, `Json`, `StreamingAssetsReader` | 1, 3 |
| `Scripts/GeoJson` | `GeoJsonParser`, `RouteBuilder` | 1 |
| `Scripts/Navigation` | `NavigationGraph` (A*), `RouteProgressTracker`, `LandmarkTriggerService`, `TurnInstructionService` | 2, 11, 13 |
| `Scripts/GPS` | `GpsManager`, `DeviceSensorService` | 3 |
| `Scripts/Localization` | `GeoAnchorFrame`, `HybridLocalizationEngine` | 4 |
| `Scripts/AR` | `ARSessionBootstrapper`, `GroundPlacementService`, `ARAnchorService`, `NavigationArrow`, `DynamicArrowManager`, `ReferenceImageRelocalizer` | 5–9, 17 |
| `Scripts/Audio` | `VoiceNavigationManager`, `AndroidTextToSpeech` | 10 |
| `Scripts/Database` | `IRepositories`, `Json/JsonDatabase` | 12 |
| `Scripts/UI` | `NavigationHUD`, `MiniMapController`, `LandmarkPopup`, `DebugOverlay`, `MainMenuController` | 14, 15, 19 |
| `Scripts/Managers` | `AppConfig`, `AppBootstrap` | 20 |
| `Scripts/Editor` | `ProjectSetup` | — |

`StreamingAssets/Database/schema.sql` holds the SQLite schema (System 12).

---

## Data notes — read before publishing results

The supplied datasets needed three corrections to be usable. All are handled in code,
and the originals are unmodified apart from two edits noted below.

**1. The route has a real 1.17 km hole.**
`alipiri_mettu.geojson` contains six walkable ways plus one footway spur. They are not
in walking order and are not all digitised in the same direction. `RouteBuilder` chains
them by endpoint proximity, reversing where needed, and correctly rejects the spur
(`way/365041854`, 3.6 km off-route). But there is **no geometry at all** between the end
of `way/367434744` (Alipiri Arch foot road) and the start of `way/30434846` (Mokallu
Mettu) — 1172 m of the walk is missing from OpenStreetMap.

The builder bridges it with a straight connector and flags those waypoints. Arrows there
render **amber, not blue**, because the guidance is interpolated rather than surveyed.
Verified totals: 7.29 km, 161 source vertices, ending 115 m from the "Alipiri Last Step"
landmark.

**2. `json_coorinates.txt` was not valid JSON** — a trailing comma after the last landmark.
Fixed in the copy at `StreamingAssets/Database/landmarks.json`.

**3. Landmark #5 (Mathsyavataram) had longitude `779.404…`** — an extra leading 7, placing
it off the planet. Corrected to `79.404…`. `AppBootstrap` additionally rejects any landmark
outside a Tirumala bounding box, so a similar typo cannot silently distort the route.

Three further defects are **left as-is** because they are content, not structure, and the
brief says not to redesign the data. They will show up in a demo:

- #31 and #32 are the same Dorasani Mandapam at identical coordinates.
  `LandmarkTriggerService` de-duplicates within 5 m so it only announces once.
- #8 (Kurma Avataram) and #28 (Sri Krishna Avataram) both read
  *"This is statue of Baktha Anjaneya Swamy Temple"* / *"Balarama Avataram"*.
- #22 (Step 1600) says *"You reaching 1500 step"*.

The GeoJSON carries **no elevation data**, so `Waypoint.elevation` is 0 throughout and
vertical placement comes entirely from AR ground raycasts. The ~500 m of real climb is not
represented in the route geometry.

---

## Deviations from the brief

**OpenCV for Unity is not used.** It is a paid Asset Store package that cannot be installed
here, and it is not needed: landmark recognition (System 5) uses ARCore's native Augmented
Images through `ARTrackedImageManager`, which is faster, runs on the HAL, and does not cost
a licence. Drop your landmark photographs into an `XRReferenceImageLibrary` and map them to
landmark ids on the `ReferenceImageRelocalizer` component. Until you add images, drift is
bounded by GPS and route snapping only.

**SQLite ships as schema + interfaces, not as the active backend.** The active
implementation is `JsonDatabase`, which needs no native plugin so the project runs on a
clean checkout. Both sit behind `IDatabase`/`IWaypointRepository`/etc., so switching is a
one-line change in `AppBootstrap` once you import a SQLite provider. `schema.sql` is the
full production schema including the per-second `navigation_trace` table you will want for
the research write-up.

**Assets not generated:** landmark photographs, recorded voice clips, and the offline map
raster. Voice falls back to Android's on-device TTS (still fully offline). The mini-map is
drawn from route geometry, so it needs no map tiles.

---

## The hybrid localisation algorithm

`HybridLocalizationEngine` + `GeoAnchorFrame` are the research contribution.

ARCore odometry and the geodetic route live in **separate coordinate frames**, joined by one
correctable yaw+translation transform. Corrections adjust the *frame*, never the arrows — so
the world shifts coherently instead of individual arrows sliding.

Five sources, each used only for what it is good at:

| Source | Role | Rate |
|---|---|---|
| ARCore VIO | primary motion | per frame |
| GPS (Kalman, CV model) | global reference | 0.04–0.25 /s by fix quality |
| Route snapping | lateral prior, perpendicular only | 0.35 /s within an 18 m corridor |
| Compass | slow yaw correction | 0.05 /s, ignored above 40° disagreement |
| Landmark images | hard drift reset | on detection, 30 s per-landmark cooldown |

GPS fixes >45 m from the tracked pose are rejected as multipath. The compass is deliberately
weak — the iron handrails on the Alipiri steps distort it badly.

`DebugOverlay` reports absorbed drift, GPS residual, and landmark fix count — the metrics
for the paper.

---

## Not yet validated on device

Route stitching, the geodesy, and the data defects above were verified against the real
files. Everything downstream — AR placement, arrow stability, tracking recovery — is written
against the AR Foundation 6.5 API but **has not been run on hardware**, because AR requires a
physical ARCore device. Expect to tune `GroundPlacementService` constants on the actual
staircase; the tread-selection heuristic is the most likely thing to need adjustment.
