using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OffWorkWidget;
using Windows.Foundation;

namespace OffWorkWidget.WinUI;

public sealed partial class MainWindow
{
    const int Koi3DShopEffect = 23;
    const double Koi3DFrameInterval = 1d / 30;
    const int Koi3DRenderWidth = 288;
    const int Koi3DRenderHeight = 162;

    Koi3DAquariumRenderer? koi3DRenderer;
    Image? koi3DImage;
    EffectParameters koi3DParameters;
    double koi3DLastFrame, koi3DNextFrame;
    long koi3DLastBreathFeedAt;

    void StartKoi3DEffect(EffectParameters parameters, double width, double height)
    {
        koi3DParameters = parameters;
        bool lightTheme = UsesDayEffectPalette();
        if (koi3DRenderer != null && koi3DImage != null)
        {
            koi3DImage.Width = width;
            koi3DImage.Height = height;
            koi3DImage.Opacity = parameters.Opacity / 100d;
            koi3DRenderer.QueueParameters(parameters, lightTheme);
            return;
        }

        koi3DRenderer = new Koi3DAquariumRenderer(Koi3DRenderWidth, Koi3DRenderHeight);
        koi3DRenderer.QueueParameters(parameters, lightTheme);
        koi3DImage = new Image
        {
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            Source = koi3DRenderer.Bitmap,
            Opacity = parameters.Opacity / 100d,
            IsHitTestVisible = false
        };
        ambientEffects.Children.Insert(0, koi3DImage);

        koi3DLastFrame = elapsed.Elapsed.TotalSeconds;
        koi3DNextFrame = koi3DLastFrame;
        koi3DRenderer.ScheduleFrame(0.016f, false, 0f, 0f, lightTheme, parameters);
        CompositionTarget.Rendering += AnimateKoi3DEffect;
    }

    void StopKoi3DEffect()
    {
        if (koi3DRenderer == null && koi3DImage == null) return;
        CompositionTarget.Rendering -= AnimateKoi3DEffect;
        if (koi3DImage != null)
        {
            ambientEffects.Children.Remove(koi3DImage);
            koi3DImage = null;
        }
        koi3DRenderer?.Dispose();
        koi3DRenderer = null;
    }

    void HandleKoi3DClick(double logicalX, double logicalY)
    {
        if (koi3DRenderer == null) return;
        double width = surface.Width / PhysicalScale;
        double height = surface.Height / PhysicalScale;
        if (width < 32 || height < 32) return;
        float nx = (float)Math.Clamp(logicalX / width, 0, 1);
        float ny = (float)Math.Clamp(logicalY / height, 0, 1);
        koi3DRenderer.EnqueueClick(nx, ny);
    }

    void TriggerKoi3DBreathFeed()
    {
        if (koi3DRenderer == null) return;
        long now = elapsed.ElapsedMilliseconds;
        if (now - koi3DLastBreathFeedAt < 2600) return;
        koi3DLastBreathFeedAt = now;
        koi3DRenderer.EnqueueAutoFood(2);
    }

    void AnimateKoi3DEffect(object? sender, object args)
    {
        if (disposed || exiting || !uiSettings.AnimationsEnabled || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            ClearAmbientShopEffects();
            return;
        }
        if (koi3DRenderer == null || koi3DImage == null) return;

        // Pause 3D updates while dragging so desktop glass capture and window motion stay silky smooth.
        if (isDragging)
        {
            koi3DLastFrame = elapsed.Elapsed.TotalSeconds;
            return;
        }

        // Fast UI-thread present (~0.1ms) only when the background worker has finished a new frame.
        koi3DRenderer.TryPresentOnUiThread();

        double now = elapsed.Elapsed.TotalSeconds;
        if (now < koi3DNextFrame) return;
        float dt = (float)Math.Min(now - koi3DLastFrame, 0.05);
        koi3DLastFrame = now;
        koi3DNextFrame = Math.Max(koi3DNextFrame + Koi3DFrameInterval, now + Koi3DFrameInterval * 0.5);

        double width = surface.Width / PhysicalScale;
        double height = surface.Height / PhysicalScale;
        if (width < 32 || height < 32) return;
        if (Math.Abs(koi3DImage.Width - width) > 0.5 || Math.Abs(koi3DImage.Height - height) > 0.5)
        {
            koi3DImage.Width = width;
            koi3DImage.Height = height;
        }
        koi3DImage.Opacity = koi3DParameters.Opacity / 100d;

        Point pointer = default;
        bool hasPointer = TryGetHoverPoint(out pointer);
        float ndcX = 99f, ndcY = 99f;
        bool pointerInside = false;
        if (hasPointer)
        {
            double px = pointer.X / PhysicalScale;
            double py = pointer.Y / PhysicalScale;
            if (px >= 0 && px <= width && py >= 0 && py <= height)
            {
                pointerInside = true;
                ndcX = (float)(px / width * 2.0 - 1.0);
                ndcY = (float)(-(py / height * 2.0 - 1.0));
            }
        }

        if (ambientClick is Point clickPoint)
        {
            ambientClick = null;
            koi3DRenderer.EnqueueClick(
                (float)Math.Clamp(clickPoint.X / width, 0, 1),
                (float)Math.Clamp(clickPoint.Y / height, 0, 1));
        }

        koi3DRenderer.ScheduleFrame(dt, pointerInside, ndcX, ndcY, UsesDayEffectPalette(), koi3DParameters);
    }

    // =========================================================================
    // Native C# 3D Software-Rasterized Neo-Chinese Gongbi Koi Aquarium Engine
    // Runs physics & rasterization off the UI thread with double buffering.
    // =========================================================================
    sealed class Koi3DAquariumRenderer : IDisposable
    {
        const float TankW = 11.2f, TankH = 5.6f, TankD = 4.8f;
        const float WaterY = 2.15f, FloorY = -2.18f;
        const float SwimHalfW = 4.35f, SwimMinY = -1.82f, SwimMaxY = 1.92f, SwimHalfD = 1.85f;
        const int MaxSmallFish = 12;

        readonly int width, height;
        readonly byte[] frameBuffer;     // Written exclusively by background worker
        readonly byte[] presentBuffer;   // Read exclusively by UI thread
        readonly float[] zBuffer;
        readonly WriteableBitmap bitmap;
        readonly Stream pixelStream;
        readonly object syncLock = new();
        int renderInFlight;
        volatile bool hasPendingPresent;
        volatile bool disposed;

        // Pending input commands from UI thread -> background worker
        bool pendingClick;
        float pendingClickNx, pendingClickNy;
        int pendingAutoFood;

        // Cached theme backgrounds (BGRA premultiplied)
        readonly byte[] bgLight;
        readonly byte[] bgDark;

        // Procedural textures (ARGB 32-bit packed)
        readonly int texSize = 256;
        readonly uint[] koiBodyTex;
        readonly uint[] koiTailTex;
        readonly uint[] koiFinTex;
        readonly uint[][] miniKoiTex;
        readonly uint[] rockeryTex;
        readonly uint[] substrateTex;

        // Camera
        readonly Vector3 baseCamPos = new(0f, 1.25f, 10.6f);
        Vector3 camPos = new(0f, 1.25f, 10.6f);
        Matrix4x4 viewProj;
        Matrix4x4 invViewProj;

        // Static & animated 3D meshes
        readonly MeshData floorMesh;
        readonly List<StaticMeshInstance> staticMeshes = new();
        readonly List<PlantBlade> plants = new();

        // Big Hero Koi
        readonly BigKoiState bigKoi;

        // School of Mini-Koi
        readonly MeshData miniBodyMesh;
        readonly MeshData miniTailMesh;
        readonly List<SmallKoiState> smallFish = new();
        int activeSmallFishCount = 10;

        // Dynamic aquarium elements
        readonly Vector3 oxygenPos = new(3.15f, FloorY + 0.04f, -0.42f);
        readonly List<BubbleState> bubbles = new();
        readonly List<FoodPellet> pellets = new();
        readonly List<SurfaceRipple> ripples = new();
        readonly List<SplashDrop> splashDrops = new();
        readonly List<PendingRipple> pendingRipples = new();

        float simTime;
        float oxygenTimer;
        float oxygenRippleTimer;
        float autoFeedTimer = 3.5f;
        float fishScale = 1.0f;
        float windBias = 0f;

        // Scratch arrays for vertex transforms (zero per-frame GC)
        Vector3[] scratchPos = new Vector3[1024];
        Vector3[] scratchNorm = new Vector3[1024];
        Vector4[] scratchClip = new Vector4[1024];

        internal WriteableBitmap Bitmap => bitmap;

        internal Koi3DAquariumRenderer(int width, int height)
        {
            this.width = width;
            this.height = height;
            frameBuffer = new byte[width * height * 4];
            presentBuffer = new byte[width * height * 4];
            zBuffer = new float[width * height];
            bitmap = new WriteableBitmap(width, height);
            pixelStream = bitmap.PixelBuffer.AsStream();

            bgLight = BuildBackWallBuffer(width, height, light: true);
            bgDark = BuildBackWallBuffer(width, height, light: false);

            koiBodyTex = BuildChineseKoiTexture(texSize);
            koiTailTex = BuildChineseFinTexture(texSize, isTail: true);
            koiFinTex = BuildChineseFinTexture(texSize, isTail: false);
            miniKoiTex = new[]
            {
                BuildMiniKoiTexture(128, 0),
                BuildMiniKoiTexture(128, 1),
                BuildMiniKoiTexture(128, 2)
            };
            rockeryTex = BuildRockeryTexture(texSize);
            substrateTex = BuildSubstrateTexture(texSize);

            floorMesh = BuildFloorMesh();
            BuildRockeryAndHardware();
            BuildWaterPlants();
            bigKoi = BuildBigKoi();

            miniBodyMesh = BuildKoiBodyMesh(0.44f, 0.135f, 0.112f, 8, 6);
            miniTailMesh = BuildVeilTailMesh(0.24f, 0.21f, 3, 3);
            InitSmallFish();
            SeedInitialBubbles();
            UpdateCameraMatrix(0f, 0f, false, 0.016f);
        }

        internal void QueueParameters(EffectParameters parameters, bool lightTheme)
        {
            activeSmallFishCount = Math.Clamp(parameters.Count - 1, 1, MaxSmallFish);
            fishScale = (float)Math.Clamp(parameters.Size, 0.5, 1.8);
            windBias = (float)Math.Clamp(parameters.Wind, -1.5, 1.5);
        }

        internal void EnqueueClick(float nx, float ny)
        {
            lock (syncLock)
            {
                pendingClick = true;
                pendingClickNx = nx;
                pendingClickNy = ny;
            }
        }

        internal void EnqueueAutoFood(int count)
        {
            lock (syncLock)
            {
                pendingAutoFood = Math.Min(8, pendingAutoFood + count);
            }
        }

        internal void TryPresentOnUiThread()
        {
            if (disposed || !hasPendingPresent) return;
            lock (syncLock)
            {
                if (disposed || !hasPendingPresent) return;
                hasPendingPresent = false;
                pixelStream.Position = 0;
                pixelStream.Write(presentBuffer, 0, presentBuffer.Length);
            }
            bitmap.Invalidate();
        }

        internal void ScheduleFrame(float dt, bool pointerInside, float ndcX, float ndcY, bool lightTheme, EffectParameters parameters)
        {
            if (disposed) return;
            if (Interlocked.CompareExchange(ref renderInFlight, 1, 0) != 0) return;

            Task.Run(() =>
            {
                try
                {
                    if (disposed) return;
                    QueueParameters(parameters, lightTheme);

                    bool doClick = false;
                    float clickNx = 0f, clickNy = 0f;
                    int extraFood = 0;
                    lock (syncLock)
                    {
                        if (pendingClick)
                        {
                            doClick = true;
                            clickNx = pendingClickNx;
                            clickNy = pendingClickNy;
                            pendingClick = false;
                        }
                        if (pendingAutoFood > 0)
                        {
                            extraFood = pendingAutoFood;
                            pendingAutoFood = 0;
                        }
                    }

                    if (doClick) ProcessClick(clickNx, clickNy);
                    if (extraFood > 0) DropFood(null, null, extraFood);

                    float speedMul = (float)Math.Clamp(parameters.Speed, 0.3, 2.2);
                    float sdt = dt * speedMul;
                    simTime += sdt;

                    UpdateCameraMatrix(ndcX, ndcY, pointerInside, dt);
                    UpdateFoodPellets(sdt, simTime);
                    UpdateBigKoi(sdt, simTime, pointerInside, ndcX, ndcY);
                    UpdateSmallFish(sdt, simTime);
                    UpdateEnvironment(sdt, simTime);

                    RenderScene(lightTheme);

                    lock (syncLock)
                    {
                        if (!disposed)
                        {
                            Buffer.BlockCopy(frameBuffer, 0, presentBuffer, 0, frameBuffer.Length);
                            hasPendingPresent = true;
                        }
                    }
                }
                finally
                {
                    Volatile.Write(ref renderInFlight, 0);
                }
            });
        }

        void ProcessClick(float nx, float ny)
        {
            float ndcX = nx * 2f - 1f;
            float ndcY = -(ny * 2f - 1f);
            GetCameraRay(ndcX, ndcY, out Vector3 rayOrigin, out Vector3 rayDir);

            if (RaySphereIntersect(rayOrigin, rayDir, bigKoi.Pos, 1.25f * fishScale))
            {
                StartleBigKoi(rayOrigin + rayDir * 8f);
                return;
            }

            float hitX = 0f;
            if (MathF.Abs(rayDir.Z) > 0.001f)
            {
                float tHit = -rayOrigin.Z / rayDir.Z;
                if (tHit > 0.5f && tHit < 30f)
                    hitX = rayOrigin.X + rayDir.X * tHit;
            }
            DropFood(hitX, (RandFloat() - 0.5f) * 1.1f, 8);
        }

        void DropFood(float? centerX, float? centerZ, int count)
        {
            if (pellets.Count >= 26) return;
            float dropX = centerX.HasValue ? Math.Clamp(centerX.Value, -3.0f, 3.0f) : (RandFloat() - 0.5f) * 4.2f;
            float dropZ = centerZ.HasValue ? Math.Clamp(centerZ.Value, -1.1f, 1.1f) : (RandFloat() - 0.5f) * 1.6f;

            for (int i = 0; i < count && pellets.Count < 26; i++)
            {
                float px = Math.Clamp(dropX + (RandFloat() - 0.5f) * 1.9f, -SwimHalfW + 0.75f, SwimHalfW - 0.75f);
                float pz = Math.Clamp(dropZ + (RandFloat() - 0.5f) * 1.2f, -SwimHalfD + 0.5f, SwimHalfD - 0.5f);
                float py = WaterY + 0.45f + RandFloat() * 0.75f;
                pellets.Add(new FoodPellet
                {
                    Pos = new Vector3(px, py, pz),
                    Vel = new Vector3((RandFloat() - 0.5f) * 0.42f, -2.4f - RandFloat() * 0.6f, (RandFloat() - 0.5f) * 0.42f),
                    SinkSpeed = 0.58f + RandFloat() * 0.34f,
                    WobblePhase = RandFloat() * MathF.Tau,
                    Radius = 0.052f + RandFloat() * 0.02f,
                    ColorVariant = i % 3
                });
            }
        }

        // ---------------------------------------------------------------------
        // Camera & Raycasting
        // ---------------------------------------------------------------------
        void UpdateCameraMatrix(float ndcX, float ndcY, bool pointerInside, float dt)
        {
            float targetX = pointerInside ? ndcX * 0.42f : 0f;
            float targetY = baseCamPos.Y + (pointerInside ? ndcY * 0.24f : 0f);
            float lerp = MathF.Min(1f, dt * 4.2f);
            camPos.X += (targetX - camPos.X) * lerp;
            camPos.Y += (targetY - camPos.Y) * lerp;
            camPos.Z = baseCamPos.Z;

            Matrix4x4 view = Matrix4x4.CreateLookAt(camPos, new Vector3(0f, -0.06f, 0f), Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(32f * MathF.PI / 180f, (float)width / height, 0.1f, 60f);
            viewProj = view * proj;
            Matrix4x4.Invert(viewProj, out invViewProj);
        }

        void GetCameraRay(float ndcX, float ndcY, out Vector3 origin, out Vector3 dir)
        {
            Vector4 nearClip = Vector4.Transform(new Vector4(ndcX, ndcY, 0f, 1f), invViewProj);
            Vector4 farClip = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invViewProj);
            Vector3 nearPt = new(nearClip.X / nearClip.W, nearClip.Y / nearClip.W, nearClip.Z / nearClip.W);
            Vector3 farPt = new(farClip.X / farClip.W, farClip.Y / farClip.W, farClip.Z / farClip.W);
            origin = nearPt;
            dir = SafeNormalize(farPt - nearPt, -Vector3.UnitZ);
        }

        static bool RaySphereIntersect(Vector3 origin, Vector3 dir, Vector3 center, float radius)
        {
            Vector3 oc = origin - center;
            float b = Vector3.Dot(oc, dir);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            return b * b - c >= 0f;
        }

        // ---------------------------------------------------------------------
        // Physics: Food, Big Koi, Small Fish, Environment
        // ---------------------------------------------------------------------
        void UpdateFoodPellets(float dt, float t)
        {
            autoFeedTimer -= dt;
            if (autoFeedTimer <= 0f)
            {
                if (pellets.Count < 3) DropFood(null, null, 7 + Random.Shared.Next(3));
                autoFeedTimer = 11f + RandFloat() * 6f;
            }

            for (int i = pellets.Count - 1; i >= 0; i--)
            {
                var p = pellets[i];
                if (p.Eaten) { pellets.RemoveAt(i); continue; }
                p.Age += dt;

                if (!p.InWater)
                {
                    p.Vel.Y -= 8.2f * dt;
                    p.Pos += p.Vel * dt;
                    if (p.Pos.Y <= WaterY)
                    {
                        p.Pos.Y = WaterY - 0.04f;
                        p.InWater = true;
                        p.Vel = new Vector3(p.Vel.X * 0.35f, -1.15f, p.Vel.Z * 0.35f);
                        SpawnRipple(p.Pos.X, p.Pos.Z, 1.35f, 0.88f);
                        pendingRipples.Add(new PendingRipple { X = p.Pos.X, Z = p.Pos.Z, Delay = 0.16f, MaxScale = 1.02f, Opacity = 0.68f });
                        SpawnSplash(p.Pos.X, p.Pos.Z, 3);
                        SpawnBubble(p.Pos.X, WaterY - 0.06f, p.Pos.Z, 0.018f, 0.45f, 1.0f);
                    }
                }
                else
                {
                    p.Vel.Y += (-p.SinkSpeed - p.Vel.Y) * MathF.Min(1f, dt * 3.8f);
                    p.Pos.Y += p.Vel.Y * dt;
                    p.Pos.X += (p.Vel.X + MathF.Sin(t * 1.6f + p.WobblePhase) * 0.11f + windBias * 0.12f) * dt;
                    p.Pos.Z += (p.Vel.Z + MathF.Cos(t * 1.4f + p.WobblePhase) * 0.09f) * dt;
                    p.Vel.X *= 0.96f;
                    p.Vel.Z *= 0.96f;
                    if (p.Pos.Y <= FloorY + 0.08f) { p.Pos.Y = FloorY + 0.08f; p.Vel.Y = 0f; }
                    if (p.Age > 20f) pellets.RemoveAt(i);
                }
            }
        }

        void ConsumePellet(FoodPellet pellet, Vector3 eaterPos, bool isBig)
        {
            if (pellet.Eaten) return;
            pellet.Eaten = true;
            pellets.Remove(pellet);

            int bCount = isBig ? 5 : 2;
            for (int b = 0; b < bCount; b++)
            {
                SpawnBubble(
                    eaterPos.X + (RandFloat() - 0.5f) * 0.24f,
                    eaterPos.Y + (RandFloat() - 0.5f) * 0.14f,
                    eaterPos.Z + (RandFloat() - 0.5f) * 0.24f,
                    0.016f + RandFloat() * 0.022f,
                    0.55f + RandFloat() * 0.35f,
                    1.8f);
            }
            if (pellet.Pos.Y > WaterY - 0.55f)
                SpawnRipple(pellet.Pos.X, pellet.Pos.Z, isBig ? 1.6f : 0.95f, 0.58f);
        }

        FoodPellet? FindNearestFood(Vector3 pos, out float bestDist)
        {
            FoodPellet? best = null;
            bestDist = float.PositiveInfinity;
            for (int i = 0; i < pellets.Count; i++)
            {
                var p = pellets[i];
                if (p.Eaten || !p.InWater) continue;
                float d = Vector3.Distance(pos, p.Pos);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        void StartleBigKoi(Vector3 cursorWorld)
        {
            bool wasCalm = bigKoi.FleeEnergy < 0.25f;
            bigKoi.FleeEnergy = 1.0f;
            bigKoi.State = KoiBehavior.Flee;

            if (wasCalm || bigKoi.FleeBurstTimer <= 0f)
            {
                Vector3 away = bigKoi.Pos - cursorWorld;
                if (away.LengthSquared() < 0.01f)
                    away = new Vector3(RandFloat() - 0.5f, RandFloat() - 0.5f, RandFloat() - 0.5f);
                away = SafeNormalize(away, Vector3.UnitX);

                Vector3 esc = SafeNormalize(new Vector3(
                    away.X + (RandFloat() - 0.5f) * 1.3f,
                    away.Y + (RandFloat() - 0.5f) * 0.7f,
                    (RandFloat() - 0.5f) * 1.4f), Vector3.UnitX);
                bigKoi.FleeTarget = new Vector3(
                    Math.Clamp(bigKoi.Pos.X + esc.X * 3.8f, -SwimHalfW + 1.2f, SwimHalfW - 1.2f),
                    Math.Clamp(bigKoi.Pos.Y + esc.Y * 3.8f, SwimMinY + 0.55f, SwimMaxY - 0.55f),
                    Math.Clamp(bigKoi.Pos.Z + esc.Z * 3.8f, -SwimHalfD + 0.75f, SwimHalfD - 0.75f));
                bigKoi.FleeBurstTimer = 0.30f + RandFloat() * 0.22f;

                if (wasCalm) bigKoi.Vel += esc * 4.0f;
                for (int i = 0; i < 5; i++)
                {
                    SpawnBubble(
                        bigKoi.Pos.X - bigKoi.Dir.X * 0.75f + (RandFloat() - 0.5f) * 0.3f,
                        bigKoi.Pos.Y + (RandFloat() - 0.5f) * 0.25f,
                        bigKoi.Pos.Z - bigKoi.Dir.Z * 0.75f + (RandFloat() - 0.5f) * 0.3f,
                        0.018f + RandFloat() * 0.026f, 0.65f + RandFloat() * 0.45f, 2.0f);
                }
            }
        }

        void UpdateBigKoi(float dt, float t, bool pointerInside, float ndcX, float ndcY)
        {
            Vector3 cursorWorld = Vector3.Zero;
            bool isHovered = false;
            if (pointerInside)
            {
                GetCameraRay(ndcX, ndcY, out Vector3 rOrig, out Vector3 rDir);
                isHovered = RaySphereIntersect(rOrig, rDir, bigKoi.Pos, 1.30f * fishScale);
                if (MathF.Abs(rDir.Z) > 0.001f)
                {
                    float tPlane = (bigKoi.Pos.Z - rOrig.Z) / rDir.Z;
                    cursorWorld = rOrig + rDir * Math.Clamp(tPlane, 1f, 30f);
                }
            }

            if (isHovered)
            {
                StartleBigKoi(cursorWorld);
            }
            else if (bigKoi.FleeEnergy > 0f)
            {
                bigKoi.FleeEnergy = MathF.Max(0f, bigKoi.FleeEnergy - dt * 0.56f);
                if (bigKoi.FleeEnergy <= 0.08f && bigKoi.State == KoiBehavior.Flee)
                {
                    bigKoi.State = KoiBehavior.Cruise;
                    bigKoi.WanderTarget = PickRandomTankPoint(1.35f, 0.6f, 0.65f);
                }
            }

            FoodPellet? nearestFood = FindNearestFood(bigKoi.Pos, out float foodDist);
            if (bigKoi.State != KoiBehavior.Flee)
            {
                if (nearestFood != null) bigKoi.State = KoiBehavior.Feed;
                else if (bigKoi.State == KoiBehavior.Feed) bigKoi.State = KoiBehavior.Cruise;
            }

            float targetSpeed = 1.12f, steerResp = 2.2f;
            Vector3 desired;
            bigKoi.MouthOpen = 0f;

            if (bigKoi.State == KoiBehavior.Flee)
            {
                bigKoi.FleeBurstTimer -= dt;
                if (Vector3.Distance(bigKoi.Pos, bigKoi.FleeTarget) < 1.2f || bigKoi.FleeBurstTimer <= 0f)
                {
                    Vector3 farCorner = PickRandomTankPoint(1.2f, 0.55f, 0.65f);
                    if (pointerInside && Vector3.Distance(farCorner, cursorWorld) < 2.4f)
                        farCorner.X = -MathF.Sign(cursorWorld.X == 0f ? 1f : cursorWorld.X) * (1.8f + RandFloat() * 1.2f);
                    bigKoi.FleeTarget = farCorner;
                    bigKoi.FleeBurstTimer = 0.32f + RandFloat() * 0.24f;
                }
                desired = bigKoi.FleeTarget - bigKoi.Pos;
                targetSpeed = 2.9f + bigKoi.FleeEnergy * 3.3f;
                steerResp = 7.5f;

                bigKoi.BubbleTimer -= dt;
                if (bigKoi.BubbleTimer <= 0f)
                {
                    bigKoi.BubbleTimer = 0.09f;
                    SpawnBubble(bigKoi.Pos.X - bigKoi.Dir.X * 0.9f, bigKoi.Pos.Y + (RandFloat() - 0.5f) * 0.18f, bigKoi.Pos.Z - bigKoi.Dir.Z * 0.9f, 0.016f + RandFloat() * 0.018f, 0.55f, 1.6f);
                }
            }
            else if (bigKoi.State == KoiBehavior.Feed && nearestFood != null)
            {
                desired = nearestFood.Pos - bigKoi.Pos;
                targetSpeed = foodDist < 2.0f ? 3.55f : 2.95f;
                steerResp = 6.2f;

                Vector3 mouthPos = bigKoi.Pos + bigKoi.SmoothDir * (0.98f * fishScale);
                float mouthDist = Vector3.Distance(mouthPos, nearestFood.Pos);
                if (mouthDist < 1.15f)
                    bigKoi.MouthOpen = Math.Clamp((1.15f - mouthDist) / 0.6f, 0f, 1f);
                if (mouthDist < 0.68f || foodDist < 1.05f)
                {
                    ConsumePellet(nearestFood, mouthPos, isBig: true);
                    bigKoi.GulpTimer = 0.30f;
                }
            }
            else
            {
                bigKoi.WanderTimer -= dt;
                if (bigKoi.WanderTimer <= 0f || Vector3.Distance(bigKoi.Pos, bigKoi.WanderTarget) < 1.05f)
                {
                    bigKoi.WanderTarget = PickRandomTankPoint(1.35f, 0.6f, 0.65f);
                    bigKoi.WanderTimer = 3.2f + RandFloat() * 3.0f;
                }
                desired = bigKoi.WanderTarget - bigKoi.Pos;
                float glidePulse = 0.5f + 0.5f * MathF.Sin(t * 0.92f + MathF.Sin(t * 0.37f) * 1.4f);
                targetSpeed = 0.78f + glidePulse * 0.58f;
                steerResp = 1.85f;
            }

            if (bigKoi.GulpTimer > 0f)
            {
                bigKoi.GulpTimer -= dt;
                bigKoi.MouthOpen = MathF.Max(bigKoi.MouthOpen, MathF.Sin(Math.Clamp(bigKoi.GulpTimer / 0.30f, 0f, 1f) * MathF.PI));
            }

            if (desired.LengthSquared() > 0.0001f)
                desired = Vector3.Normalize(desired) * targetSpeed;
            desired += ComputeSoftWallSteer(bigKoi.Pos, 1.4f, 0.52f, 0.82f, bigKoi.State == KoiBehavior.Flee ? 8.0f : 4.0f);
            desired.X += windBias * 0.18f;

            bigKoi.Vel = Vector3.Lerp(bigKoi.Vel, desired, MathF.Min(1f, steerResp * dt));
            float speed = bigKoi.Vel.Length();
            float maxAllowed = bigKoi.State == KoiBehavior.Flee ? 6.4f : (bigKoi.State == KoiBehavior.Feed ? 3.8f : 1.58f);
            if (speed > maxAllowed) bigKoi.Vel *= maxAllowed / speed;

            bigKoi.Pos += bigKoi.Vel * dt;
            if (bigKoi.Vel.LengthSquared() > 0.0025f) bigKoi.Dir = Vector3.Normalize(bigKoi.Vel);
            EnforceTankRebound(ref bigKoi.Pos, ref bigKoi.Vel, bigKoi.Dir, ref bigKoi.FleeTarget, ref bigKoi.WanderTarget, bigKoi.BodyLen * 0.5f * fishScale, bigKoi.TailLen * fishScale, 0.38f * fishScale, isBig: true);
            if (bigKoi.Vel.LengthSquared() > 0.0025f) bigKoi.Dir = Vector3.Normalize(bigKoi.Vel);

            bigKoi.SmoothDir = SafeNormalize(Vector3.Lerp(bigKoi.SmoothDir, bigKoi.Dir, MathF.Min(1f, dt * (bigKoi.State == KoiBehavior.Flee ? 9.2f : 5.2f))), bigKoi.Dir);
            float yaw = MathF.Atan2(-bigKoi.SmoothDir.Z, bigKoi.SmoothDir.X);
            float pitch = MathF.Asin(Math.Clamp(bigKoi.SmoothDir.Y, -0.42f, 0.42f));

            float dYaw = yaw - bigKoi.LastYaw;
            while (dYaw > MathF.PI) dYaw -= MathF.Tau;
            while (dYaw < -MathF.PI) dYaw += MathF.Tau;
            float clampedYawRate = Math.Clamp(dYaw / MathF.Max(0.012f, dt), -3.2f, 3.2f);
            bigKoi.YawRate += (clampedYawRate - bigKoi.YawRate) * MathF.Min(1f, dt * 5.2f);
            bigKoi.LastYaw = yaw;

            float targetRoll = Math.Clamp(-bigKoi.YawRate * 0.065f, -0.18f, 0.18f);
            bigKoi.Roll += (targetRoll - bigKoi.Roll) * MathF.Min(1f, dt * 5.5f);
            bigKoi.Yaw = yaw;
            bigKoi.Pitch = pitch;

            float curSpeed = bigKoi.Vel.Length();
            float glideWave = 0.5f + 0.5f * MathF.Sin(t * 0.92f + MathF.Sin(t * 0.37f) * 1.4f);
            float targetEffort = bigKoi.State == KoiBehavior.Flee ? 1.55f : (bigKoi.State == KoiBehavior.Feed ? 1.22f : (0.42f + glideWave * 0.68f + MathF.Abs(bigKoi.YawRate) * 0.22f));
            bigKoi.SwimEffort += (targetEffort - bigKoi.SwimEffort) * MathF.Min(1f, dt * 4.5f);

            float wagFreq = 3.2f + curSpeed * 3.1f + bigKoi.SwimEffort * 2.2f + bigKoi.FleeEnergy * 5.2f;
            bigKoi.WagPhase += wagFreq * dt;

            float wagAmp = 0.115f + bigKoi.SwimEffort * 0.065f + MathF.Min(0.085f, curSpeed * 0.016f);
            float targetBend = Math.Clamp(bigKoi.YawRate * 0.24f, -0.95f, 0.95f);
            bigKoi.BodyBend += (targetBend - bigKoi.BodyBend) * MathF.Min(1f, dt * 5.5f);

            DeformBigKoiMeshes(bigKoi.WagPhase, wagAmp, bigKoi.BodyBend, bigKoi.SwimEffort);
        }

        void UpdateSmallFish(float dt, float t)
        {
            int count = Math.Min(activeSmallFishCount, smallFish.Count);
            for (int i = 0; i < count; i++)
            {
                var f = smallFish[i];
                FoodPellet? nearestFood = FindNearestFood(f.Pos, out float foodDist);

                Vector3 sep = Vector3.Zero, ali = Vector3.Zero, coh = Vector3.Zero;
                int neighbors = 0;
                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    var o = smallFish[j];
                    Vector3 diff = f.Pos - o.Pos;
                    float distSq = diff.LengthSquared();
                    if (distSq < 2.2f && distSq > 0.0001f)
                    {
                        float d = MathF.Sqrt(distSq);
                        neighbors++;
                        ali += o.Vel;
                        coh += o.Pos;
                        if (d < 0.58f) sep += diff * ((0.58f - d) / d);
                    }
                }

                Vector3 accel = Vector3.Zero;
                if (neighbors > 0)
                {
                    ali = SafeNormalize(ali / neighbors, f.Dir) * (nearestFood != null ? 0.38f : 0.80f);
                    coh = (coh / neighbors - f.Pos) * (nearestFood != null ? 0.16f : 0.40f);
                    accel += ali + coh + sep * 2.6f;
                }

                Vector3 fromBig = f.Pos - bigKoi.Pos;
                float distBig = fromBig.Length();
                float avoidR = bigKoi.State == KoiBehavior.Flee ? 2.2f : (bigKoi.State == KoiBehavior.Feed ? 1.55f : 1.15f);
                if (distBig < avoidR && distBig > 0.01f)
                {
                    float push = (1f - distBig / avoidR) * (bigKoi.State == KoiBehavior.Flee ? 8.5f : (bigKoi.State == KoiBehavior.Feed ? 5.2f : 3.2f));
                    accel += SafeNormalize(fromBig, Vector3.UnitX) * push;
                }

                if (nearestFood != null)
                {
                    Vector3 toFood = nearestFood.Pos - f.Pos;
                    if (toFood.LengthSquared() > 0.0001f) accel += Vector3.Normalize(toFood) * 4.8f;
                    if (foodDist < 0.30f) ConsumePellet(nearestFood, f.Pos, isBig: false);
                }
                else
                {
                    accel.X += MathF.Sin(t * 0.9f + i * 2.1f) * 0.48f + windBias * 0.15f;
                    accel.Y += MathF.Cos(t * 0.8f + i * 1.7f) * 0.20f;
                    accel.Z += MathF.Sin(t * 0.7f + i * 3.3f) * 0.38f;
                }

                accel += ComputeSoftWallSteer(f.Pos, 0.65f, 0.38f, 0.52f, 5.8f);
                f.Vel += accel * (dt * 2.2f);
                float spd = f.Vel.Length();
                float maxSpd = nearestFood != null ? f.MaxSpeed * 1.55f : (bigKoi.State == KoiBehavior.Flee && distBig < avoidR ? f.MaxSpeed * 1.85f : f.MaxSpeed);
                if (spd > maxSpd) f.Vel *= maxSpd / spd;
                else if (spd < 0.52f && spd > 0.001f) f.Vel *= 0.52f / spd;

                f.Pos += f.Vel * dt;
                if (spd > 0.02f) f.Dir = Vector3.Normalize(f.Vel);
                Vector3 dummy1 = Vector3.Zero, dummy2 = Vector3.Zero;
                EnforceTankRebound(ref f.Pos, ref f.Vel, f.Dir, ref dummy1, ref dummy2, 0.24f, 0.22f, 0.14f, isBig: false);

                float curSpd = f.Vel.Length();
                if (curSpd > 0.04f)
                    f.SmoothDir = SafeNormalize(Vector3.Lerp(f.SmoothDir, SafeNormalize(f.Vel, f.Dir), MathF.Min(1f, dt * 8.5f)), f.Dir);
                f.WagPhase += (10f + curSpd * 6.5f) * dt;
            }
        }

        void UpdateEnvironment(float dt, float t)
        {
            oxygenTimer -= dt;
            const float interval = 0.065f;
            while (oxygenTimer <= 0f)
            {
                oxygenTimer += interval * (0.7f + RandFloat() * 0.6f);
                float ang = RandFloat() * MathF.Tau;
                float rad = MathF.Sqrt(RandFloat()) * 0.20f;
                bool isFine = RandFloat() < 0.68f;
                float r = isFine ? (0.015f + RandFloat() * 0.015f) : (0.028f + RandFloat() * 0.024f);
                SpawnBubble(
                    oxygenPos.X + MathF.Cos(ang) * rad,
                    oxygenPos.Y + 0.085f,
                    oxygenPos.Z + MathF.Sin(ang) * rad,
                    r, 0.75f + r * 20f + RandFloat() * 0.35f, 5.5f);
            }

            oxygenRippleTimer -= dt;
            if (oxygenRippleTimer <= 0f)
            {
                oxygenRippleTimer = 0.55f;
                SpawnRipple(oxygenPos.X + (RandFloat() - 0.5f) * 0.42f, oxygenPos.Z + (RandFloat() - 0.5f) * 0.42f, 0.68f + RandFloat() * 0.32f, 0.42f);
            }

            for (int i = pendingRipples.Count - 1; i >= 0; i--)
            {
                var pr = pendingRipples[i];
                pr.Delay -= dt;
                if (pr.Delay <= 0f)
                {
                    SpawnRipple(pr.X, pr.Z, pr.MaxScale, pr.Opacity);
                    pendingRipples.RemoveAt(i);
                }
                else pendingRipples[i] = pr;
            }

            for (int i = splashDrops.Count - 1; i >= 0; i--)
            {
                var d = splashDrops[i];
                d.Age += dt;
                d.Vel.Y -= 7.8f * dt;
                d.Pos += d.Vel * dt;
                if (d.Pos.Y <= WaterY || d.Age > 0.45f) splashDrops.RemoveAt(i);
                else splashDrops[i] = d;
            }

            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var b = bubbles[i];
                b.Age += dt;
                b.Pos.Y += b.Vy * dt;
                b.Pos.X += (b.Vx + MathF.Sin(t * b.WobbleFreq + b.WobblePhase) * b.WobbleAmp + windBias * 0.18f) * dt;
                b.Pos.Z += (b.Vz + MathF.Cos(t * (b.WobbleFreq * 0.88f) + b.WobblePhase) * b.WobbleAmp) * dt;
                b.Vx *= 0.96f; b.Vz *= 0.96f;
                if (b.Pos.Y >= WaterY - 0.03f || b.Age >= b.Life) bubbles.RemoveAt(i);
                else bubbles[i] = b;
            }

            for (int i = ripples.Count - 1; i >= 0; i--)
            {
                var rp = ripples[i];
                rp.Age += dt;
                if (rp.Age >= rp.MaxAge) ripples.RemoveAt(i);
                else ripples[i] = rp;
            }

            for (int p = 0; p < plants.Count; p++)
            {
                var blade = plants[p];
                var pos = blade.Mesh.Positions;
                var orig = blade.OrigPositions;
                for (int i = 0; i < pos.Length; i++)
                {
                    float frac = orig[i].Y / blade.Height;
                    float bend = MathF.Sin(t * 1.18f + blade.Phase + frac * 2.1f) * blade.SwayAmp * frac * frac
                               + MathF.Sin(t * 2.05f + blade.Phase * 1.3f + frac * 3.5f) * blade.SwayAmp * 0.26f * frac
                               + windBias * 0.16f * frac * frac;
                    pos[i] = new Vector3(orig[i].X + bend, orig[i].Y, orig[i].Z + bend * 0.38f);
                }
            }
        }

        void SpawnBubble(float x, float y, float z, float radius, float vy, float life)
        {
            if (bubbles.Count > 90) return;
            bubbles.Add(new BubbleState
            {
                Pos = new Vector3(x, y, z),
                Radius = radius,
                Vy = vy,
                Vx = (RandFloat() - 0.5f) * 0.22f,
                Vz = (RandFloat() - 0.5f) * 0.22f,
                WobblePhase = RandFloat() * MathF.Tau,
                WobbleFreq = 3.4f + RandFloat() * 3.2f,
                WobbleAmp = 0.14f + radius * 3.5f,
                Life = life
            });
        }

        void SpawnRipple(float x, float z, float maxScale, float opacity)
        {
            if (ripples.Count > 20) return;
            ripples.Add(new SurfaceRipple { X = x, Z = z, Age = 0f, MaxAge = 1.35f, MaxScale = maxScale, BaseOpacity = opacity });
        }

        void SpawnSplash(float x, float z, int count)
        {
            if (splashDrops.Count > 20) return;
            for (int i = 0; i < count; i++)
            {
                float ang = RandFloat() * MathF.Tau;
                float spd = 0.28f + RandFloat() * 0.42f;
                splashDrops.Add(new SplashDrop
                {
                    Pos = new Vector3(x + (RandFloat() - 0.5f) * 0.06f, WaterY + 0.02f, z + (RandFloat() - 0.5f) * 0.06f),
                    Vel = new Vector3(MathF.Cos(ang) * spd, 0.95f + RandFloat() * 0.65f, MathF.Sin(ang) * spd)
                });
            }
        }

        static Vector3 PickRandomTankPoint(float marginX, float marginY, float marginZ) => new(
            (RandFloat() - 0.5f) * MathF.Max(1.0f, (SwimHalfW - marginX) * 2f),
            SwimMinY + marginY + RandFloat() * MathF.Max(0.6f, (SwimMaxY - SwimMinY) - marginY * 2f),
            (RandFloat() - 0.5f) * MathF.Max(0.6f, (SwimHalfD - marginZ) * 2f));

        static Vector3 ComputeSoftWallSteer(Vector3 pos, float marginX, float marginY, float marginZ, float strength)
        {
            Vector3 s = Vector3.Zero;
            float maxX = SwimHalfW - marginX, maxY = SwimMaxY - marginY, minY = SwimMinY + marginY, maxZ = SwimHalfD - marginZ;
            if (pos.X > maxX) s.X -= ((pos.X - maxX) / marginX) * strength;
            if (pos.X < -maxX) s.X += ((-maxX - pos.X) / marginX) * strength;
            if (pos.Y > maxY) s.Y -= ((pos.Y - maxY) / marginY) * strength;
            if (pos.Y < minY) s.Y += ((minY - pos.Y) / marginY) * strength;
            if (pos.Z > maxZ) s.Z -= ((pos.Z - maxZ) / marginZ) * strength;
            if (pos.Z < -maxZ) s.Z += ((-maxZ - pos.Z) / marginZ) * strength;
            return s;
        }

        void EnforceTankRebound(ref Vector3 pos, ref Vector3 vel, Vector3 dir, ref Vector3 fleeTarget, ref Vector3 wanderTarget, float halfBody, float tailExtra, float sideRadius, bool isBig)
        {
            float padX = MathF.Max(sideRadius, MathF.Abs(dir.X) * (halfBody + tailExtra * 0.52f)) + 0.10f;
            float padY = MathF.Max(sideRadius, MathF.Abs(dir.Y) * (halfBody + tailExtra * 0.36f)) + 0.08f;
            float padZ = MathF.Max(sideRadius, MathF.Abs(dir.Z) * (halfBody + tailExtra * 0.52f)) + 0.10f;

            float maxX = MathF.Max(0.4f, SwimHalfW - padX);
            float maxZ = MathF.Max(0.35f, SwimHalfD - padZ);
            float maxY = SwimMaxY - padY;
            float minY = SwimMinY + padY;
            float boost = isBig ? 0.86f : 0.84f;
            float minReb = isBig ? 0.62f : 0.42f;

            if (pos.X > maxX) { pos.X = maxX; vel.X = -MathF.Max(minReb, MathF.Abs(vel.X) * boost); if (isBig) { fleeTarget.X = -MathF.Abs(fleeTarget.X); wanderTarget.X = -MathF.Abs(wanderTarget.X); } }
            else if (pos.X < -maxX) { pos.X = -maxX; vel.X = MathF.Max(minReb, MathF.Abs(vel.X) * boost); if (isBig) { fleeTarget.X = MathF.Abs(fleeTarget.X); wanderTarget.X = MathF.Abs(wanderTarget.X); } }

            if (pos.Y > maxY) { pos.Y = maxY; vel.Y = -MathF.Max(minReb * 0.65f, MathF.Abs(vel.Y) * boost); }
            else if (pos.Y < minY) { pos.Y = minY; vel.Y = MathF.Max(minReb * 0.65f, MathF.Abs(vel.Y) * boost); }

            if (pos.Z > maxZ) { pos.Z = maxZ; vel.Z = -MathF.Max(minReb, MathF.Abs(vel.Z) * boost); if (isBig) { fleeTarget.Z = -MathF.Abs(fleeTarget.Z); wanderTarget.Z = -MathF.Abs(wanderTarget.Z); } }
            else if (pos.Z < -maxZ) { pos.Z = -maxZ; vel.Z = MathF.Max(minReb, MathF.Abs(vel.Z) * boost); if (isBig) { fleeTarget.Z = MathF.Abs(fleeTarget.Z); wanderTarget.Z = MathF.Abs(wanderTarget.Z); } }
        }

        // ---------------------------------------------------------------------
        // Scene Rendering Pipeline (Background Thread)
        // ---------------------------------------------------------------------
        void RenderScene(bool light)
        {
            Buffer.BlockCopy(light ? bgLight : bgDark, 0, frameBuffer, 0, frameBuffer.Length);
            Array.Fill(zBuffer, float.PositiveInfinity);

            Vector3 keyDir = Vector3.Normalize(new Vector3(0.32f, 0.86f, 0.48f));
            Vector3 rimDir = Vector3.Normalize(new Vector3(-0.45f, 0.54f, -0.68f));
            Vector3 fogCol = light ? new Vector3(221f, 227f, 223f) : new Vector3(11f, 28f, 34f);
            float fogDensity = light ? 0.018f : 0.032f;

            DrawGodRays(light);
            DrawTexturedMesh(floorMesh, Matrix4x4.CreateTranslation(0f, FloorY - 0.12f, 0f), substrateTex, texSize, keyDir, rimDir, fogCol, fogDensity, light, cullBack: true, opaque: true, alphaScale: 1f, causticBoost: light ? 0.14f : 0.30f);
            DrawKoiFloorShadow(light);

            for (int i = 0; i < staticMeshes.Count; i++)
            {
                var inst = staticMeshes[i];
                if (inst.Texture != null)
                    DrawTexturedMesh(inst.Mesh, inst.World, inst.Texture, inst.TexSize, keyDir, rimDir, fogCol, fogDensity, light, cullBack: true, opaque: true, alphaScale: 1f, causticBoost: 0f);
                else
                    DrawSolidMesh(inst.Mesh, inst.World, inst.SolidColor, keyDir, rimDir, fogCol, fogDensity, light, cullBack: true, opaque: true, alpha: 1f);
            }

            for (int i = 0; i < plants.Count; i++)
            {
                var pl = plants[i];
                DrawSolidMesh(pl.Mesh, pl.World, pl.Color, keyDir, rimDir, fogCol, fogDensity, light, cullBack: false, opaque: true, alpha: 0.90f);
            }

            for (int i = 0; i < pellets.Count; i++)
            {
                var p = pellets[i];
                if (p.Eaten) continue;
                uint pCol = p.ColorVariant == 0 ? 0xFF9C5E28u : (p.ColorVariant == 1 ? 0xFFB87333u : 0xFF7E481Eu);
                DrawBillboardSphere(p.Pos, p.Radius, pCol, 1.0f, isBubble: false, light);
            }

            int sCount = Math.Min(activeSmallFishCount, smallFish.Count);
            for (int i = 0; i < sCount; i++)
            {
                var f = smallFish[i];
                float yaw = MathF.Atan2(-f.SmoothDir.Z, f.SmoothDir.X);
                float pitch = MathF.Asin(Math.Clamp(f.SmoothDir.Y, -0.52f, 0.52f));
                Matrix4x4 rot = Matrix4x4.CreateRotationZ(pitch) * Matrix4x4.CreateRotationY(yaw);
                Matrix4x4 world = Matrix4x4.CreateScale(f.Scale * (0.7f + 0.3f * fishScale)) * rot * Matrix4x4.CreateTranslation(f.Pos);

                DrawTexturedMesh(miniBodyMesh, world, miniKoiTex[f.Variant], 128, keyDir, rimDir, fogCol, fogDensity, light, cullBack: true, opaque: true, alphaScale: 1f, causticBoost: 0f);

                Matrix4x4 tailLocal = Matrix4x4.CreateRotationY(MathF.Sin(f.WagPhase) * 0.46f) * Matrix4x4.CreateTranslation(-0.21f, 0f, 0f);
                DrawTexturedMesh(miniTailMesh, tailLocal * world, koiFinTex, texSize, keyDir, rimDir, fogCol, fogDensity, light, cullBack: false, opaque: false, alphaScale: 0.80f, causticBoost: 0f);
            }

            float breath = bigKoi.State == KoiBehavior.Cruise ? MathF.Sin(simTime * 1.35f) * 0.016f : 0f;
            Matrix4x4 koiRot = Matrix4x4.CreateRotationX(bigKoi.Roll) * Matrix4x4.CreateRotationZ(bigKoi.Pitch) * Matrix4x4.CreateRotationY(bigKoi.Yaw);
            Matrix4x4 koiWorld = Matrix4x4.CreateScale(fishScale) * koiRot * Matrix4x4.CreateTranslation(bigKoi.Pos.X, bigKoi.Pos.Y + breath, bigKoi.Pos.Z);

            DrawTexturedMesh(bigKoi.BodyMesh, koiWorld, koiBodyTex, texSize, keyDir, rimDir, fogCol, fogDensity, light, cullBack: true, opaque: true, alphaScale: 1f, causticBoost: 0f);

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 eyeWorld = Vector3.Transform(new Vector3(0.80f, 0.055f, side * 0.21f), koiWorld);
                DrawBillboardSphere(eyeWorld, 0.072f * fishScale, 0xFFDF421Cu, 1.0f, isBubble: false, light);
                Vector3 pupilWorld = Vector3.Transform(new Vector3(0.82f, 0.058f, side * 0.245f), koiWorld);
                DrawBillboardSphere(pupilWorld, 0.048f * fishScale, 0xFF10141Au, 1.0f, isBubble: false, light);
            }

            if (bigKoi.MouthOpen > 0.08f)
            {
                Vector3 jawWorld = Vector3.Transform(new Vector3(1.06f, -0.04f, 0f), koiWorld);
                DrawBillboardSphere(jawWorld, (0.045f + bigKoi.MouthOpen * 0.055f) * fishScale, 0xFF3B1214u, 0.92f, isBubble: false, light);
            }

            Matrix4x4 tailWorld = Matrix4x4.CreateTranslation(-bigKoi.BodyLen * 0.47f, 0f, 0f) * koiWorld;
            DrawTexturedMesh(bigKoi.TailMesh, tailWorld, koiTailTex, texSize, keyDir, rimDir, fogCol, fogDensity, light, cullBack: false, opaque: false, alphaScale: 0.92f, causticBoost: 0f);

            Matrix4x4 dorsalWorld = Matrix4x4.CreateTranslation(-0.08f, bigKoi.BodyH * 0.41f, 0f) * koiWorld;
            DrawTexturedMesh(bigKoi.DorsalMesh, dorsalWorld, koiFinTex, texSize, keyDir, rimDir, fogCol, fogDensity, light, cullBack: false, opaque: false, alphaScale: 0.86f, causticBoost: 0f);

            float curSpeed = bigKoi.Vel.Length();
            float tuck = Math.Clamp((curSpeed - 1.5f) / 3.6f, 0f, 1f);
            float rowMain = MathF.Sin(bigKoi.WagPhase * 0.52f) * (0.28f * (1f - tuck * 0.55f));
            float rowFeather = MathF.Cos(bigKoi.WagPhase * 0.52f) * (0.15f * (1f - tuck * 0.5f));
            float turnTrim = Math.Clamp(bigKoi.YawRate * 0.12f, -0.22f, 0.22f);

            Matrix4x4 pecLWorld = Matrix4x4.CreateRotationZ(-0.18f + rowFeather * 0.45f) * Matrix4x4.CreateRotationY(0.52f - tuck * 0.34f + rowMain - turnTrim) * Matrix4x4.CreateRotationX(0.74f + rowFeather * 0.55f) * Matrix4x4.CreateTranslation(0.42f, -0.11f, 0.20f) * koiWorld;
            DrawTexturedMesh(bigKoi.PecLMesh, pecLWorld, koiFinTex, texSize, keyDir, rimDir, fogCol, fogDensity, light, cullBack: false, opaque: false, alphaScale: 0.85f, causticBoost: 0f);

            Matrix4x4 pecRWorld = Matrix4x4.CreateRotationZ(-0.18f - rowFeather * 0.45f) * Matrix4x4.CreateRotationY(-0.52f + tuck * 0.34f - rowMain - turnTrim) * Matrix4x4.CreateRotationX(-0.74f - rowFeather * 0.55f) * Matrix4x4.CreateTranslation(0.42f, -0.11f, -0.20f) * koiWorld;
            DrawTexturedMesh(bigKoi.PecRMesh, pecRWorld, koiFinTex, texSize, keyDir, rimDir, fogCol, fogDensity, light, cullBack: false, opaque: false, alphaScale: 0.85f, causticBoost: 0f);

            uint bubCol = light ? 0xFFFFFFFFu : 0xFFEEFCFCu;
            for (int i = 0; i < bubbles.Count; i++)
            {
                var b = bubbles[i];
                float r = b.Radius * (1f + MathF.Min(0.25f, b.Age * 0.06f));
                DrawBillboardSphere(b.Pos, r, bubCol, light ? 0.65f : 0.54f, isBubble: true, light);
            }
            for (int i = 0; i < splashDrops.Count; i++)
            {
                var d = splashDrops[i];
                DrawBillboardSphere(d.Pos, 0.024f, light ? 0xFF607A80u : 0xFFEEFCFCu, 0.72f, isBubble: false, light);
            }

            DrawWaterSurfaceAndRipples(light);
        }

        void DrawGodRays(bool light)
        {
            ReadOnlySpan<float> rayX = stackalloc float[] { 0.28f, 0.52f, 0.76f };
            ReadOnlySpan<float> rayW = stackalloc float[] { 0.14f, 0.17f, 0.13f };
            float baseAlpha = light ? 0.15f : 0.12f;

            for (int r = 0; r < 3; r++)
            {
                float cx = (rayX[r] + MathF.Sin(simTime * 0.36f + r * 1.9f) * 0.022f) * width;
                float halfW = rayW[r] * width;
                float invHalfW = 1f / halfW;
                float rayAlpha = baseAlpha + MathF.Sin(simTime * 0.72f + r * 1.9f) * 0.03f;

                for (int y = 0; y < height; y++)
                {
                    float fy = (float)y / height;
                    float vFade = (1f - fy) * (1f - fy * 0.55f) * rayAlpha;
                    float shift = cx + y * 0.19f;
                    int x0 = Math.Max(0, (int)(shift - halfW));
                    int x1 = Math.Min(width - 1, (int)(shift + halfW));
                    int rowOffset = y * width * 4;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x - shift) * invHalfW;
                        float hFade = 1f - dx * dx;
                        if (hFade <= 0f) continue;
                        float a = vFade * hFade * hFade;
                        int idx = rowOffset + x * 4;
                        if (light)
                        {
                            frameBuffer[idx + 0] = (byte)Math.Min(255, frameBuffer[idx + 0] + (int)(245 * a));
                            frameBuffer[idx + 1] = (byte)Math.Min(255, frameBuffer[idx + 1] + (int)(250 * a));
                            frameBuffer[idx + 2] = (byte)Math.Min(255, frameBuffer[idx + 2] + (int)(255 * a));
                        }
                        else
                        {
                            frameBuffer[idx + 0] = (byte)Math.Min(255, frameBuffer[idx + 0] + (int)(210 * a));
                            frameBuffer[idx + 1] = (byte)Math.Min(255, frameBuffer[idx + 1] + (int)(242 * a));
                            frameBuffer[idx + 2] = (byte)Math.Min(255, frameBuffer[idx + 2] + (int)(238 * a));
                        }
                    }
                }
            }
        }

        void DrawKoiFloorShadow(bool light)
        {
            Vector3 shadowPos = new(bigKoi.Pos.X - 0.18f, FloorY - 0.05f, bigKoi.Pos.Z - 0.12f);
            if (!ProjectPoint(shadowPos, out float sx, out float sy, out float sz, out float invW)) return;
            float rx = 1.15f * fishScale * invW * width * 0.42f;
            float ry = 0.32f * fishScale * invW * height * 0.45f;
            int minX = Math.Max(0, (int)(sx - rx));
            int maxX = Math.Min(width - 1, (int)(sx + rx));
            int minY = Math.Max(0, (int)(sy - ry));
            int maxY = Math.Min(height - 1, (int)(sy + ry));
            float heightFactor = Math.Clamp(1f - (bigKoi.Pos.Y - FloorY) / 5.2f, 0.25f, 0.85f);
            float maxDark = (light ? 0.22f : 0.34f) * heightFactor;

            for (int y = minY; y <= maxY; y++)
            {
                float dy = (y - sy) / MathF.Max(1f, ry);
                int row = y * width;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - sx) / MathF.Max(1f, rx);
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= 1f) continue;
                    int pIdx = row + x;
                    if (sz > zBuffer[pIdx] + 0.02f) continue;
                    float k = 1f - (1f - d2) * maxDark;
                    int bIdx = pIdx * 4;
                    frameBuffer[bIdx + 0] = (byte)(frameBuffer[bIdx + 0] * k);
                    frameBuffer[bIdx + 1] = (byte)(frameBuffer[bIdx + 1] * k);
                    frameBuffer[bIdx + 2] = (byte)(frameBuffer[bIdx + 2] * k);
                }
            }
        }

        void DrawWaterSurfaceAndRipples(bool light)
        {
            int surfH = (int)(height * 0.14f);
            for (int y = 0; y < surfH; y++)
            {
                float fy = (float)y / surfH;
                float wave = MathF.Sin(fy * 6f + simTime * 1.8f) * 0.5f + 0.5f;
                float a = (1f - fy) * (light ? 0.10f : 0.13f) * (0.7f + 0.3f * wave);
                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    float shimmer = MathF.Sin(x * 0.06f + simTime * 1.6f) * 0.5f + 0.5f;
                    float pa = a * (0.65f + 0.35f * shimmer);
                    int idx = row + x * 4;
                    if (light)
                    {
                        frameBuffer[idx + 0] = (byte)Math.Min(255, frameBuffer[idx + 0] + (int)(225 * pa));
                        frameBuffer[idx + 1] = (byte)Math.Min(255, frameBuffer[idx + 1] + (int)(240 * pa));
                        frameBuffer[idx + 2] = (byte)Math.Min(255, frameBuffer[idx + 2] + (int)(236 * pa));
                    }
                    else
                    {
                        frameBuffer[idx + 0] = (byte)Math.Min(255, frameBuffer[idx + 0] + (int)(185 * pa));
                        frameBuffer[idx + 1] = (byte)Math.Min(255, frameBuffer[idx + 1] + (int)(225 * pa));
                        frameBuffer[idx + 2] = (byte)Math.Min(255, frameBuffer[idx + 2] + (int)(220 * pa));
                    }
                }
            }

            for (int i = 0; i < ripples.Count; i++)
            {
                var rp = ripples[i];
                float p = rp.Age / rp.MaxAge;
                float ease = 1f - MathF.Pow(1f - p, 2.2f);
                float radiusWorld = 0.15f + ease * rp.MaxScale * 0.85f;
                float alpha = MathF.Pow(1f - p, 1.35f) * rp.BaseOpacity;
                if (alpha < 0.02f) continue;

                if (!ProjectPoint(new Vector3(rp.X, WaterY, rp.Z), out float cx, out float cy, out _, out float invW))
                    continue;
                float rx = radiusWorld * invW * width * 0.92f;
                float ry = rx * 0.24f;
                if (rx < 2f) continue;

                int steps = Math.Clamp((int)(rx * 2.2f), 18, 60);
                byte rCol = light ? (byte)52 : (byte)228;
                byte gCol = light ? (byte)84 : (byte)250;
                byte bCol = light ? (byte)90 : (byte)246;
                for (int s = 0; s < steps; s++)
                {
                    float ang = s * MathF.Tau / steps;
                    int px = (int)(cx + MathF.Cos(ang) * rx);
                    int py = (int)(cy + MathF.Sin(ang) * ry);
                    BlendPixel(px, py, bCol, gCol, rCol, alpha * 0.85f);
                    BlendPixel(px, py + 1, 255, 255, 255, alpha * (light ? 0.55f : 0.35f));
                }
            }
        }

        void DrawBillboardSphere(Vector3 worldPos, float worldRadius, uint argb, float alpha, bool isBubble, bool light)
        {
            if (!ProjectPoint(worldPos, out float sx, out float sy, out float sz, out float invW)) return;
            float rPx = MathF.Max(1.1f, worldRadius * invW * width * 0.86f);
            int minX = Math.Max(0, (int)(sx - rPx));
            int maxX = Math.Min(width - 1, (int)(sx + rPx));
            int minY = Math.Max(0, (int)(sy - rPx));
            int maxY = Math.Min(height - 1, (int)(sy + rPx));

            float baseR = (argb >> 16) & 0xFF;
            float baseG = (argb >> 8) & 0xFF;
            float baseB = argb & 0xFF;

            for (int y = minY; y <= maxY; y++)
            {
                float dy = (y - sy) / rPx;
                int row = y * width;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - sx) / rPx;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > 1f) continue;
                    int pIdx = row + x;
                    if (sz > zBuffer[pIdx]) continue;

                    if (isBubble)
                    {
                        float rim = MathF.Pow(d2, 1.4f);
                        float spec = MathF.Max(0f, 1f - ((dx + 0.35f) * (dx + 0.35f) + (dy + 0.35f) * (dy + 0.35f)) * 4.5f);
                        float pa = alpha * (0.16f + 0.72f * rim + 0.9f * spec);
                        if (light)
                        {
                            float b = baseB * (0.72f + 0.28f * spec) - rim * 65f;
                            float g = baseG * (0.76f + 0.24f * spec) - rim * 55f;
                            float r = baseR * (0.78f + 0.22f * spec) - rim * 50f;
                            BlendPixelRaw(pIdx, (byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(r, 0, 255), MathF.Min(1f, pa));
                        }
                        else
                        {
                            BlendPixelRaw(pIdx, (byte)Math.Clamp(baseB, 0, 255), (byte)Math.Clamp(baseG, 0, 255), (byte)Math.Clamp(baseR, 0, 255), MathF.Min(1f, pa));
                        }
                    }
                    else
                    {
                        float nz = MathF.Sqrt(MathF.Max(0f, 1f - d2));
                        float diff = Math.Clamp(0.45f + 0.55f * (-dx * 0.35f - dy * 0.55f + nz * 0.75f), 0.25f, 1.15f);
                        if (alpha >= 0.99f)
                        {
                            zBuffer[pIdx] = sz;
                            int idx = pIdx * 4;
                            frameBuffer[idx + 0] = (byte)Math.Clamp(baseB * diff, 0, 255);
                            frameBuffer[idx + 1] = (byte)Math.Clamp(baseG * diff, 0, 255);
                            frameBuffer[idx + 2] = (byte)Math.Clamp(baseR * diff, 0, 255);
                            frameBuffer[idx + 3] = 255;
                        }
                        else
                        {
                            BlendPixelRaw(pIdx, (byte)Math.Clamp(baseB * diff, 0, 255), (byte)Math.Clamp(baseG * diff, 0, 255), (byte)Math.Clamp(baseR * diff, 0, 255), alpha);
                        }
                    }
                }
            }
        }

        bool ProjectPoint(Vector3 worldPos, out float sx, out float sy, out float sz, out float invW)
        {
            Vector4 clip = Vector4.Transform(new Vector4(worldPos, 1f), viewProj);
            if (clip.W <= 0.1f) { sx = sy = sz = invW = 0f; return false; }
            invW = 1f / clip.W;
            sx = (clip.X * invW * 0.5f + 0.5f) * width;
            sy = (0.5f - clip.Y * invW * 0.5f) * height;
            sz = clip.Z * invW;
            return true;
        }

        void EnsureScratch(int count)
        {
            if (scratchPos.Length < count)
            {
                int next = Math.Max(count, scratchPos.Length * 2);
                scratchPos = new Vector3[next];
                scratchNorm = new Vector3[next];
                scratchClip = new Vector4[next];
            }
        }

        void DrawTexturedMesh(MeshData mesh, Matrix4x4 world, uint[] texture, int tSize, Vector3 keyDir, Vector3 rimDir, Vector3 fogCol, float fogDensity, bool light, bool cullBack, bool opaque, float alphaScale, float causticBoost)
        {
            int vCount = mesh.Positions.Length;
            EnsureScratch(vCount);
            Matrix4x4 wvp = world * viewProj;
            for (int i = 0; i < vCount; i++)
            {
                Vector3 p = mesh.Positions[i];
                scratchPos[i] = Vector3.Transform(p, world);
                scratchNorm[i] = SafeNormalize(Vector3.TransformNormal(mesh.Normals[i], world), Vector3.UnitY);
                scratchClip[i] = Vector4.Transform(new Vector4(p, 1f), wvp);
            }

            int[] indices = mesh.Indices;
            Vector2[] uvs = mesh.Uvs;
            int mask = tSize - 1;

            for (int t = 0; t < indices.Length; t += 3)
            {
                int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
                Vector4 c0 = scratchClip[i0], c1 = scratchClip[i1], c2 = scratchClip[i2];
                if (c0.W <= 0.15f || c1.W <= 0.15f || c2.W <= 0.15f) continue;

                float iw0 = 1f / c0.W, iw1 = 1f / c1.W, iw2 = 1f / c2.W;
                float x0 = (c0.X * iw0 * 0.5f + 0.5f) * width, y0 = (0.5f - c0.Y * iw0 * 0.5f) * height, z0 = c0.Z * iw0;
                float x1 = (c1.X * iw1 * 0.5f + 0.5f) * width, y1 = (0.5f - c1.Y * iw1 * 0.5f) * height, z1 = c1.Z * iw1;
                float x2 = (c2.X * iw2 * 0.5f + 0.5f) * width, y2 = (0.5f - c2.Y * iw2 * 0.5f) * height, z2 = c2.Z * iw2;

                float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (cullBack && area <= 0f) continue;
                if (MathF.Abs(area) < 0.001f) continue;
                float invArea = 1f / area;

                int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x0, MathF.Min(x1, x2))));
                int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(x0, MathF.Max(x1, x2))));
                int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y0, MathF.Min(y1, y2))));
                int maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(y0, MathF.Max(y1, y2))));
                if (minX > maxX || minY > maxY) continue;

                float l0 = ComputeVertexLight(scratchNorm[i0], keyDir, rimDir, light, !cullBack);
                float l1 = ComputeVertexLight(scratchNorm[i1], keyDir, rimDir, light, !cullBack);
                float l2 = ComputeVertexLight(scratchNorm[i2], keyDir, rimDir, light, !cullBack);

                Vector3 wpCenter = (scratchPos[i0] + scratchPos[i1] + scratchPos[i2]) * (1f / 3f);
                float camDist = Vector3.Distance(camPos, wpCenter);
                float fogFactor = Math.Clamp(1f - MathF.Exp(-fogDensity * fogDensity * camDist * camDist), 0f, 0.72f);

                float caustic = 0f;
                if (causticBoost > 0f)
                {
                    float wv = MathF.Sin(wpCenter.X * 2.1f + simTime * 1.4f) * MathF.Cos(wpCenter.Z * 2.4f - simTime * 1.1f);
                    caustic = MathF.Max(0f, wv) * causticBoost;
                }

                Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];

                for (int py = minY; py <= maxY; py++)
                {
                    float fy = py + 0.5f;
                    int row = py * width;
                    for (int px = minX; px <= maxX; px++)
                    {
                        float fx = px + 0.5f;
                        float w0 = ((x1 - fx) * (y2 - fy) - (x2 - fx) * (y1 - fy)) * invArea;
                        float w1 = ((x2 - fx) * (y0 - fy) - (x0 - fx) * (y2 - fy)) * invArea;
                        float w2 = 1f - w0 - w1;
                        if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                        float z = w0 * z0 + w1 * z1 + w2 * z2;
                        int pIdx = row + px;
                        if (z >= zBuffer[pIdx]) continue;

                        float u = w0 * uv0.X + w1 * uv1.X + w2 * uv2.X;
                        float v = w0 * uv0.Y + w1 * uv1.Y + w2 * uv2.Y;
                        int tx = ((int)(u * tSize) & mask);
                        int ty = ((int)(v * tSize) & mask);
                        uint texel = texture[ty * tSize + tx];
                        float ta = ((texel >> 24) & 0xFF) * (1f / 255f) * alphaScale;
                        if (ta < 0.03f) continue;

                        float shade = w0 * l0 + w1 * l1 + w2 * l2 + caustic;
                        float r = ((texel >> 16) & 0xFF) * shade;
                        float g = ((texel >> 8) & 0xFF) * shade;
                        float b = (texel & 0xFF) * shade;

                        if (fogFactor > 0.005f)
                        {
                            r += (fogCol.X - r) * fogFactor;
                            g += (fogCol.Y - g) * fogFactor;
                            b += (fogCol.Z - b) * fogFactor;
                        }

                        byte rb = (byte)Math.Clamp(r, 0, 255);
                        byte gb = (byte)Math.Clamp(g, 0, 255);
                        byte bb = (byte)Math.Clamp(b, 0, 255);

                        if (opaque && ta >= 0.98f)
                        {
                            zBuffer[pIdx] = z;
                            int idx = pIdx * 4;
                            frameBuffer[idx + 0] = bb;
                            frameBuffer[idx + 1] = gb;
                            frameBuffer[idx + 2] = rb;
                            frameBuffer[idx + 3] = 255;
                        }
                        else
                        {
                            BlendPixelRaw(pIdx, bb, gb, rb, ta);
                        }
                    }
                }
            }
        }

        void DrawSolidMesh(MeshData mesh, Matrix4x4 world, uint color, Vector3 keyDir, Vector3 rimDir, Vector3 fogCol, float fogDensity, bool light, bool cullBack, bool opaque, float alpha)
        {
            int vCount = mesh.Positions.Length;
            EnsureScratch(vCount);
            Matrix4x4 wvp = world * viewProj;
            for (int i = 0; i < vCount; i++)
            {
                Vector3 p = mesh.Positions[i];
                scratchPos[i] = Vector3.Transform(p, world);
                scratchNorm[i] = SafeNormalize(Vector3.TransformNormal(mesh.Normals[i], world), Vector3.UnitY);
                scratchClip[i] = Vector4.Transform(new Vector4(p, 1f), wvp);
            }

            float baseR = (color >> 16) & 0xFF;
            float baseG = (color >> 8) & 0xFF;
            float baseB = color & 0xFF;
            int[] indices = mesh.Indices;

            for (int t = 0; t < indices.Length; t += 3)
            {
                int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
                Vector4 c0 = scratchClip[i0], c1 = scratchClip[i1], c2 = scratchClip[i2];
                if (c0.W <= 0.15f || c1.W <= 0.15f || c2.W <= 0.15f) continue;

                float iw0 = 1f / c0.W, iw1 = 1f / c1.W, iw2 = 1f / c2.W;
                float x0 = (c0.X * iw0 * 0.5f + 0.5f) * width, y0 = (0.5f - c0.Y * iw0 * 0.5f) * height, z0 = c0.Z * iw0;
                float x1 = (c1.X * iw1 * 0.5f + 0.5f) * width, y1 = (0.5f - c1.Y * iw1 * 0.5f) * height, z1 = c1.Z * iw1;
                float x2 = (c2.X * iw2 * 0.5f + 0.5f) * width, y2 = (0.5f - c2.Y * iw2 * 0.5f) * height, z2 = c2.Z * iw2;

                float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (cullBack && area <= 0f) continue;
                if (MathF.Abs(area) < 0.001f) continue;
                float invArea = 1f / area;

                int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x0, MathF.Min(x1, x2))));
                int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(x0, MathF.Max(x1, x2))));
                int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y0, MathF.Min(y1, y2))));
                int maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(y0, MathF.Max(y1, y2))));
                if (minX > maxX || minY > maxY) continue;

                float l0 = ComputeVertexLight(scratchNorm[i0], keyDir, rimDir, light, !cullBack);
                float l1 = ComputeVertexLight(scratchNorm[i1], keyDir, rimDir, light, !cullBack);
                float l2 = ComputeVertexLight(scratchNorm[i2], keyDir, rimDir, light, !cullBack);
                float shade = (l0 + l1 + l2) * (1f / 3f);

                Vector3 wpCenter = (scratchPos[i0] + scratchPos[i1] + scratchPos[i2]) * (1f / 3f);
                float camDist = Vector3.Distance(camPos, wpCenter);
                float fogFactor = Math.Clamp(1f - MathF.Exp(-fogDensity * fogDensity * camDist * camDist), 0f, 0.72f);

                float r = baseR * shade;
                float g = baseG * shade;
                float b = baseB * shade;
                if (fogFactor > 0.005f)
                {
                    r += (fogCol.X - r) * fogFactor;
                    g += (fogCol.Y - g) * fogFactor;
                    b += (fogCol.Z - b) * fogFactor;
                }
                byte rb = (byte)Math.Clamp(r, 0, 255);
                byte gb = (byte)Math.Clamp(g, 0, 255);
                byte bb = (byte)Math.Clamp(b, 0, 255);

                for (int py = minY; py <= maxY; py++)
                {
                    float fy = py + 0.5f;
                    int row = py * width;
                    for (int px = minX; px <= maxX; px++)
                    {
                        float fx = px + 0.5f;
                        float w0 = ((x1 - fx) * (y2 - fy) - (x2 - fx) * (y1 - fy)) * invArea;
                        float w1 = ((x2 - fx) * (y0 - fy) - (x0 - fx) * (y2 - fy)) * invArea;
                        float w2 = 1f - w0 - w1;
                        if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                        float z = w0 * z0 + w1 * z1 + w2 * z2;
                        int pIdx = row + px;
                        if (z >= zBuffer[pIdx]) continue;
                        if (opaque && alpha >= 0.99f)
                        {
                            zBuffer[pIdx] = z;
                            int idx = pIdx * 4;
                            frameBuffer[idx + 0] = bb;
                            frameBuffer[idx + 1] = gb;
                            frameBuffer[idx + 2] = rb;
                            frameBuffer[idx + 3] = 255;
                        }
                        else
                        {
                            if (opaque) zBuffer[pIdx] = z;
                            BlendPixelRaw(pIdx, bb, gb, rb, alpha);
                        }
                    }
                }
            }
        }

        static float ComputeVertexLight(Vector3 n, Vector3 keyDir, Vector3 rimDir, bool lightTheme, bool doubleSided)
        {
            float ndl = Vector3.Dot(n, keyDir);
            if (doubleSided) ndl = MathF.Abs(ndl);
            else ndl = MathF.Max(0f, ndl);
            float hemi = 0.5f + 0.5f * n.Y;
            float rim = MathF.Max(0f, Vector3.Dot(n, rimDir));
            return lightTheme
                ? 0.48f + hemi * 0.24f + ndl * 0.52f + rim * 0.14f
                : 0.34f + hemi * 0.22f + ndl * 0.68f + rim * 0.24f;
        }

        void BlendPixel(int x, int y, byte b, byte g, byte r, float alpha)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            BlendPixelRaw(y * width + x, b, g, r, alpha);
        }

        void BlendPixelRaw(int pixelIndex, byte b, byte g, byte r, float alpha)
        {
            int idx = pixelIndex * 4;
            float inv = 1f - alpha;
            frameBuffer[idx + 0] = (byte)(frameBuffer[idx + 0] * inv + b * alpha);
            frameBuffer[idx + 1] = (byte)(frameBuffer[idx + 1] * inv + g * alpha);
            frameBuffer[idx + 2] = (byte)(frameBuffer[idx + 2] * inv + r * alpha);
            frameBuffer[idx + 3] = 255;
        }

        // ---------------------------------------------------------------------
        // Procedural Geometry Builders
        // ---------------------------------------------------------------------
        BigKoiState BuildBigKoi()
        {
            const float bodyLen = 2.22f, bodyH = 0.68f, bodyW = 0.56f, tailLen = 1.04f;
            var body = BuildKoiBodyMesh(bodyLen, bodyH, bodyW, 20, 14);
            var tail = BuildVeilTailMesh(tailLen, 1.00f, 8, 6);
            var dorsal = BuildDorsalMesh(1.02f, 0.44f);
            var pecL = BuildPectoralMesh(0.88f, 0.54f);
            var pecR = BuildPectoralMesh(0.88f, 0.54f);

            return new BigKoiState
            {
                BodyMesh = body,
                BodyOrig = (Vector3[])body.Positions.Clone(),
                TailMesh = tail,
                TailOrig = (Vector3[])tail.Positions.Clone(),
                DorsalMesh = dorsal,
                DorsalOrig = (Vector3[])dorsal.Positions.Clone(),
                PecLMesh = pecL,
                PecRMesh = pecR,
                PecOrig = (Vector3[])pecL.Positions.Clone(),
                BodyLen = bodyLen,
                BodyH = bodyH,
                TailLen = tailLen,
                Pos = new Vector3(0.4f, 0.05f, 0.1f),
                Vel = new Vector3(-0.95f, 0.04f, 0.12f),
                Dir = new Vector3(-1f, 0f, 0f),
                SmoothDir = new Vector3(-1f, 0f, 0f),
                WanderTarget = new Vector3(-1.4f, 0.15f, 0.1f),
                WanderTimer = 2.4f,
                LastYaw = MathF.PI,
                SwimEffort = 0.75f
            };
        }

        void DeformBigKoiMeshes(float wagPhase, float wagAmp, float turnBend, float effort)
        {
            float halfL = bigKoi.BodyLen * 0.5f;
            const float waveK = 4.65f;

            var bPos = bigKoi.BodyMesh.Positions;
            var bOrig = bigKoi.BodyOrig;
            for (int i = 0; i < bPos.Length; i++)
            {
                Vector3 o = bOrig[i];
                float u = Math.Clamp((halfL - o.X) / bigKoi.BodyLen, 0f, 1f);
                float headRecoil = -MathF.Sin(wagPhase + 0.35f) * wagAmp * 0.16f * (1f - u) * (1f - u);
                float sWave = MathF.Sin(u * waveK - wagPhase) * wagAmp * MathF.Pow(u, 1.18f);
                float cBend = turnBend * MathF.Pow(u, 1.32f) * 0.42f;
                bPos[i] = new Vector3(o.X, o.Y, o.Z + headRecoil + sWave + cBend);
            }

            var tPos = bigKoi.TailMesh.Positions;
            var tOrig = bigKoi.TailOrig;
            float tailBaseBend = turnBend * 0.42f;
            for (int i = 0; i < tPos.Length; i++)
            {
                Vector3 o = tOrig[i];
                float uTail = MathF.Max(0f, -o.X / bigKoi.TailLen);
                float phaseLag = waveK + uTail * 3.25f - wagPhase;
                float primarySweep = MathF.Sin(phaseLag) * wagAmp * (1f + uTail * 1.55f);
                float silkFlutter = MathF.Sin(phaseLag * 1.15f - MathF.Abs(o.Y) * 2.4f) * (0.045f + effort * 0.03f) * uTail * uTail;
                float bendFollow = tailBaseBend * (1f + uTail * 1.05f);
                float verticalBillow = MathF.Cos(phaseLag - o.Y * 1.4f) * 0.025f * uTail;
                tPos[i] = new Vector3(o.X, o.Y + verticalBillow, o.Z + primarySweep + silkFlutter + bendFollow);
            }

            var dPos = bigKoi.DorsalMesh.Positions;
            var dOrig = bigKoi.DorsalOrig;
            for (int i = 0; i < dPos.Length; i++)
            {
                Vector3 o = dOrig[i];
                float u = Math.Clamp((halfL - (o.X - 0.08f)) / bigKoi.BodyLen, 0f, 1f);
                float finLag = u * waveK - wagPhase - o.Y * 2.6f;
                float sway = MathF.Sin(finLag) * wagAmp * (0.45f + MathF.Pow(u, 1.15f) * 0.95f);
                float bend = turnBend * MathF.Pow(u, 1.32f) * 0.42f;
                dPos[i] = new Vector3(o.X, o.Y, o.Z + sway + bend);
            }

            DeformPectoral(bigKoi.PecLMesh.Positions, bigKoi.PecOrig, wagPhase, effort, 1f);
            DeformPectoral(bigKoi.PecRMesh.Positions, bigKoi.PecOrig, wagPhase, effort, -1f);
        }

        static void DeformPectoral(Vector3[] pos, Vector3[] orig, float wagPhase, float effort, float sign)
        {
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 o = orig[i];
                float u = Math.Clamp(-o.X / 0.88f, 0f, 1f);
                float wave = MathF.Sin(u * 4.2f - wagPhase * 0.72f + sign * 0.65f) * (0.085f + effort * 0.035f) * u * u;
                pos[i] = new Vector3(o.X, o.Y + MathF.Cos(u * 3.0f - wagPhase * 0.6f) * 0.024f * u, o.Z + wave);
            }
        }

        static MeshData BuildKoiBodyMesh(float length, float maxH, float maxW, int segU, int segV)
        {
            int vCount = (segU + 1) * (segV + 1);
            var positions = new Vector3[vCount];
            var uvs = new Vector2[vCount];
            int idx = 0;
            for (int i = 0; i <= segU; i++)
            {
                float u = (float)i / segU;
                float x = (0.5f - u) * length;
                float profileH, profileW, centerY;
                if (u < 0.05f)
                {
                    float t = u / 0.05f;
                    profileH = MathF.Sin(t * MathF.PI * 0.5f) * 0.36f;
                    profileW = MathF.Sin(t * MathF.PI * 0.5f) * 0.38f;
                    centerY = -0.04f * maxH;
                }
                else
                {
                    float t = (u - 0.05f) / 0.95f;
                    profileH = MathF.Pow(MathF.Sin(MathF.Pow(t, 0.54f) * MathF.PI), 0.72f) * 0.95f + 0.07f * (1f - t);
                    profileW = MathF.Pow(MathF.Sin(MathF.Pow(t, 0.50f) * MathF.PI), 0.76f) * 0.96f + 0.05f * (1f - t);
                    centerY = MathF.Sin(MathF.Pow(u, 0.78f) * MathF.PI) * 0.055f * maxH;
                }
                if (i == 0) { profileH = 0.02f; profileW = 0.02f; }
                if (i == segU) { profileH = 0.12f; profileW = 0.058f; }

                float ry = profileH * maxH * 0.5f;
                float rz = profileW * maxW * 0.5f;
                for (int j = 0; j <= segV; j++)
                {
                    float v = (float)j / segV;
                    float theta = v * MathF.Tau;
                    float sinT = MathF.Sin(theta), cosT = MathF.Cos(theta);
                    positions[idx] = new Vector3(x, centerY + sinT * ry * (sinT > 0 ? 1.05f : 0.95f), cosT * rz * (sinT > 0 ? 1.03f : 0.94f));
                    uvs[idx] = new Vector2(u, v);
                    idx++;
                }
            }
            var indices = new List<int>(segU * segV * 6);
            for (int i = 0; i < segU; i++)
            {
                for (int j = 0; j < segV; j++)
                {
                    int a = i * (segV + 1) + j, b = (i + 1) * (segV + 1) + j;
                    int c = (i + 1) * (segV + 1) + (j + 1), d = i * (segV + 1) + (j + 1);
                    indices.Add(a); indices.Add(d); indices.Add(b);
                    indices.Add(b); indices.Add(d); indices.Add(c);
                }
            }
            return MeshData.Create(positions, uvs, indices.ToArray());
        }

        static MeshData BuildVeilTailMesh(float length, float spreadH, int segX, int segY)
        {
            var mesh = BuildPlaneMesh(length, spreadH, segX, segY, -length * 0.5f, 0f);
            for (int i = 0; i < mesh.Positions.Length; i++)
            {
                Vector3 p = mesh.Positions[i];
                float frac = Math.Clamp(-p.X / length, 0f, 1f);
                float ny = p.Y / (spreadH * 0.5f);
                float flare = 0.16f + MathF.Pow(frac, 0.65f) * 1.08f;
                float notch = MathF.Exp(-ny * ny * 4.4f) * MathF.Pow(frac, 1.1f) * length * 0.44f;
                mesh.Positions[i] = new Vector3(p.X + notch, p.Y * flare, MathF.Sin(frac * MathF.PI) * ny * ny * 0.08f);
            }
            mesh.RecomputeNormals();
            return mesh;
        }

        static MeshData BuildDorsalMesh(float length, float height)
        {
            var mesh = BuildPlaneMesh(length, height, 8, 3, 0f, height * 0.5f);
            for (int i = 0; i < mesh.Positions.Length; i++)
            {
                Vector3 p = mesh.Positions[i];
                float u = Math.Clamp((length * 0.5f - p.X) / length, 0f, 1f);
                float fy = p.Y / height;
                float arch = MathF.Pow(MathF.Sin(u * MathF.PI), 0.72f) * (1.08f - u * 0.25f);
                mesh.Positions[i] = new Vector3(p.X - fy * length * 0.34f, p.Y * arch, MathF.Sin(u * MathF.PI * 1.5f) * fy * 0.05f);
            }
            mesh.RecomputeNormals();
            return mesh;
        }

        static MeshData BuildPectoralMesh(float length, float width)
        {
            var mesh = BuildPlaneMesh(length, width, 6, 4, -length * 0.5f, 0f);
            for (int i = 0; i < mesh.Positions.Length; i++)
            {
                Vector3 p = mesh.Positions[i];
                float u = Math.Clamp(-p.X / length, 0f, 1f);
                float shape = MathF.Sin(MathF.Pow(u, 0.65f) * MathF.PI * 0.92f + 0.08f) * (0.35f + 0.88f * MathF.Sin(u * MathF.PI * 0.7f));
                mesh.Positions[i] = new Vector3(p.X, p.Y * shape, MathF.Sin(u * MathF.PI) * 0.06f);
            }
            mesh.RecomputeNormals();
            return mesh;
        }

        static MeshData BuildPlaneMesh(float w, float h, int segX, int segY, float offsetX, float offsetY)
        {
            var pos = new Vector3[(segX + 1) * (segY + 1)];
            var uvs = new Vector2[pos.Length];
            int k = 0;
            for (int iy = 0; iy <= segY; iy++)
            {
                float fy = (float)iy / segY;
                for (int ix = 0; ix <= segX; ix++)
                {
                    float fx = (float)ix / segX;
                    pos[k] = new Vector3((fx - 0.5f) * w + offsetX, (0.5f - fy) * h + offsetY, 0f);
                    uvs[k] = new Vector2(fx, fy);
                    k++;
                }
            }
            var indices = new List<int>(segX * segY * 6);
            for (int iy = 0; iy < segY; iy++)
            {
                for (int ix = 0; ix < segX; ix++)
                {
                    int a = iy * (segX + 1) + ix, b = a + 1, c = a + segX + 1, d = c + 1;
                    indices.Add(a); indices.Add(c); indices.Add(b);
                    indices.Add(b); indices.Add(c); indices.Add(d);
                }
            }
            return MeshData.Create(pos, uvs, indices.ToArray());
        }

        static MeshData BuildFloorMesh()
        {
            const int sx = 16, sz = 8;
            float fw = TankW + 1.6f, fd = TankD + 1.0f;
            var pos = new Vector3[(sx + 1) * (sz + 1)];
            var uvs = new Vector2[pos.Length];
            int k = 0;
            for (int iz = 0; iz <= sz; iz++)
            {
                float fz = (float)iz / sz;
                float z = (fz - 0.5f) * fd;
                for (int ix = 0; ix <= sx; ix++)
                {
                    float fx = (float)ix / sx;
                    float x = (fx - 0.5f) * fw;
                    float y = MathF.Sin(x * 0.48f) * 0.12f + MathF.Cos(z * 0.72f) * 0.08f + (z < 0f ? -z * 0.08f : 0f);
                    pos[k] = new Vector3(x, y, z);
                    uvs[k] = new Vector2(fx * 3f, fz * 2f);
                    k++;
                }
            }
            var indices = new List<int>(sx * sz * 6);
            for (int iz = 0; iz < sz; iz++)
            {
                for (int ix = 0; ix < sx; ix++)
                {
                    int a = iz * (sx + 1) + ix, b = a + 1, c = a + sx + 1, d = c + 1;
                    indices.Add(a); indices.Add(c); indices.Add(b);
                    indices.Add(b); indices.Add(c); indices.Add(d);
                }
            }
            return MeshData.Create(pos, uvs, indices.ToArray());
        }

        static MeshData BuildCraggyPeakMesh(float radius, float sx, float sy, float sz, float seed)
        {
            const int lat = 7, lon = 10;
            var pos = new Vector3[(lat + 1) * (lon + 1)];
            var uvs = new Vector2[pos.Length];
            int k = 0;
            for (int i = 0; i <= lat; i++)
            {
                float v = (float)i / lat;
                float phi = v * MathF.PI;
                for (int j = 0; j <= lon; j++)
                {
                    float u = (float)j / lon;
                    float theta = u * MathF.Tau;
                    float vx = MathF.Sin(phi) * MathF.Cos(theta) * radius;
                    float vy = MathF.Cos(phi) * radius;
                    float vz = MathF.Sin(phi) * MathF.Sin(theta) * radius;

                    float hNorm = (vy / radius + 1f) * 0.5f;
                    float taper = 1.18f - hNorm * 0.56f;
                    float ridge = 1f + 0.22f * MathF.Sin(vx * 3.8f + seed) * MathF.Cos(vy * 2.9f + seed)
                                     + 0.14f * MathF.Sin(vz * 4.5f - vy * 2.2f + seed * 1.7f)
                                     + 0.08f * MathF.Cos(vx * 7.2f + vz * 6.5f);
                    float yAdj = vy < -radius * 0.45f ? -radius * 0.45f + (vy + radius * 0.45f) * 0.35f : vy;
                    pos[k] = new Vector3(vx * sx * taper * ridge, yAdj * sy, vz * sz * taper * ridge);
                    uvs[k] = new Vector2(u, v);
                    k++;
                }
            }
            var indices = new List<int>(lat * lon * 6);
            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + 1, c = a + lon + 1, d = c + 1;
                    indices.Add(a); indices.Add(c); indices.Add(b);
                    indices.Add(b); indices.Add(c); indices.Add(d);
                }
            }
            return MeshData.Create(pos, uvs, indices.ToArray());
        }

        static MeshData BuildCylinderMesh(float rTop, float rBot, float h, int segs)
        {
            var pos = new Vector3[(segs + 1) * 2 + 2];
            var uvs = new Vector2[pos.Length];
            for (int i = 0; i <= segs; i++)
            {
                float a = i * MathF.Tau / segs;
                float ca = MathF.Cos(a), sa = MathF.Sin(a);
                pos[i] = new Vector3(ca * rTop, h * 0.5f, sa * rTop);
                pos[segs + 1 + i] = new Vector3(ca * rBot, -h * 0.5f, sa * rBot);
            }
            int topCenter = (segs + 1) * 2;
            pos[topCenter] = new Vector3(0f, h * 0.5f, 0f);
            pos[topCenter + 1] = new Vector3(0f, -h * 0.5f, 0f);
            var indices = new List<int>(segs * 12);
            for (int i = 0; i < segs; i++)
            {
                int t0 = i, t1 = i + 1, b0 = segs + 1 + i, b1 = segs + 1 + i + 1;
                indices.Add(t0); indices.Add(b0); indices.Add(t1);
                indices.Add(t1); indices.Add(b0); indices.Add(b1);
                indices.Add(topCenter); indices.Add(t0); indices.Add(t1);
            }
            return MeshData.Create(pos, uvs, indices.ToArray());
        }

        void BuildRockeryAndHardware()
        {
            staticMeshes.Add(new StaticMeshInstance(BuildCraggyPeakMesh(1.05f, 1.15f, 1.75f, 0.88f, 1.4f), Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(3.45f, FloorY + 0.92f, -1.55f), rockeryTex, texSize, 0));
            staticMeshes.Add(new StaticMeshInstance(BuildCraggyPeakMesh(0.82f, 1.18f, 1.28f, 0.82f, 4.2f), Matrix4x4.CreateRotationZ(0.16f) * Matrix4x4.CreateRotationY(-0.5f) * Matrix4x4.CreateTranslation(2.38f, FloorY + 0.58f, -1.35f), rockeryTex, texSize, 0));
            staticMeshes.Add(new StaticMeshInstance(BuildCraggyPeakMesh(0.72f, 1.32f, 0.86f, 0.95f, 7.8f), Matrix4x4.CreateRotationY(1.2f) * Matrix4x4.CreateTranslation(4.25f, FloorY + 0.35f, -1.12f), rockeryTex, texSize, 0));
            staticMeshes.Add(new StaticMeshInstance(BuildCraggyPeakMesh(0.46f, 1.38f, 0.56f, 0.92f, 9.1f), Matrix4x4.CreateTranslation(2.68f, FloorY + 0.14f, -0.68f), rockeryTex, texSize, 0));
            staticMeshes.Add(new StaticMeshInstance(BuildCraggyPeakMesh(0.76f, 1.12f, 1.18f, 0.80f, 12.6f), Matrix4x4.CreateRotationZ(-0.12f) * Matrix4x4.CreateRotationY(0.8f) * Matrix4x4.CreateTranslation(-3.72f, FloorY + 0.52f, -1.62f), rockeryTex, texSize, 0));
            staticMeshes.Add(new StaticMeshInstance(BuildCraggyPeakMesh(0.52f, 1.35f, 0.58f, 0.88f, 15.3f), Matrix4x4.CreateTranslation(-2.95f, FloorY + 0.18f, -1.32f), rockeryTex, texSize, 0));
            staticMeshes.Add(new StaticMeshInstance(BuildCraggyPeakMesh(0.48f, 1.48f, 0.44f, 0.85f, 19.2f), Matrix4x4.CreateTranslation(0.42f, FloorY + 0.10f, -1.65f), rockeryTex, texSize, 0));

            staticMeshes.Add(new StaticMeshInstance(BuildCylinderMesh(0.30f, 0.34f, 0.10f, 10), Matrix4x4.CreateTranslation(oxygenPos), null, 0, 0xFF455056u));
            staticMeshes.Add(new StaticMeshInstance(BuildCylinderMesh(0.24f, 0.25f, 0.05f, 10), Matrix4x4.CreateTranslation(oxygenPos.X, oxygenPos.Y + 0.055f, oxygenPos.Z), null, 0, 0xFFD8E0E2u));
        }

        void BuildWaterPlants()
        {
            AddPlantCluster(4.28f, -0.82f, 6, 3.05f, new uint[] { 0xFF24594Bu, 0xFF2F6C5Au, 0xFF3B7B67u }, 0.72f, 0.50f, 0.20f);
            AddPlantCluster(1.56f, -0.86f, 5, 2.35f, new uint[] { 0xFF2A6252u, 0xFF367360u, 0xFF225245u }, 0.68f, 0.48f, -0.20f);
            AddPlantCluster(-4.04f, -1.50f, 5, 2.45f, new uint[] { 0xFF245246u, 0xFF2E6354u }, 0.62f, 0.44f, -0.14f);
            AddPlantCluster(-2.42f, -1.48f, 4, 1.60f, new uint[] { 0xFF2F6152u, 0xFF39705Fu }, 0.50f, 0.38f, 0.08f);
        }

        void AddPlantCluster(float baseX, float baseZ, int count, float maxH, uint[] palette, float spreadX, float spreadZ, float leanBias)
        {
            for (int i = 0; i < count; i++)
            {
                float h = maxH * (0.50f + RandFloat() * 0.50f);
                float w = 0.12f + RandFloat() * 0.08f;
                var mesh = BuildPlaneMesh(w, h, 1, 6, 0f, h * 0.5f);
                float archLean = leanBias + (RandFloat() - 0.5f) * 0.36f;
                float sWave = (RandFloat() - 0.5f) * 0.24f;
                float sFreq = 2.6f + RandFloat() * 1.4f;
                for (int v = 0; v < mesh.Positions.Length; v++)
                {
                    Vector3 p = mesh.Positions[v];
                    float frac = Math.Clamp(p.Y / h, 0f, 1f);
                    float taper = MathF.Sin(frac * MathF.PI * 0.86f + 0.14f) * (1f - frac * 0.72f);
                    float curveX = archLean * frac * frac * h * 0.24f + MathF.Sin(frac * sFreq) * sWave * frac;
                    float depthZ = MathF.Sin(frac * MathF.PI) * 0.08f * (i % 2 == 0 ? 1f : -1f);
                    mesh.Positions[v] = new Vector3(p.X * taper + curveX, p.Y, p.Z + depthZ);
                }
                mesh.RecomputeNormals();
                Matrix4x4 world = Matrix4x4.CreateRotationY((RandFloat() - 0.5f) * 1.15f)
                    * Matrix4x4.CreateTranslation(baseX + (RandFloat() - 0.5f) * spreadX, FloorY - 0.10f, baseZ + (RandFloat() - 0.5f) * spreadZ);
                plants.Add(new PlantBlade
                {
                    Mesh = mesh,
                    OrigPositions = (Vector3[])mesh.Positions.Clone(),
                    World = world,
                    Height = h,
                    Phase = RandFloat() * MathF.Tau,
                    SwayAmp = 0.16f + RandFloat() * 0.15f,
                    Color = palette[i % palette.Length]
                });
            }
        }

        void InitSmallFish()
        {
            for (int i = 0; i < MaxSmallFish; i++)
            {
                float ang = RandFloat() * MathF.Tau;
                Vector3 vel = new Vector3(MathF.Cos(ang), (RandFloat() - 0.5f) * 0.16f, MathF.Sin(ang)) * 1.1f;
                Vector3 dir = SafeNormalize(vel, Vector3.UnitX);
                smallFish.Add(new SmallKoiState
                {
                    Pos = new Vector3((RandFloat() - 0.5f) * SwimHalfW * 1.5f, -1.0f + RandFloat() * 2.0f, (RandFloat() - 0.5f) * SwimHalfD * 1.3f),
                    Vel = vel,
                    Dir = dir,
                    SmoothDir = dir,
                    WagPhase = RandFloat() * MathF.Tau,
                    MaxSpeed = 1.25f + RandFloat() * 0.28f,
                    Scale = 0.84f + RandFloat() * 0.30f,
                    Variant = i % 3
                });
            }
        }

        void SeedInitialBubbles()
        {
            for (int i = 0; i < 28; i++)
            {
                float ang = RandFloat() * MathF.Tau;
                float rad = MathF.Sqrt(RandFloat()) * 0.20f;
                float r = RandFloat() < 0.65f ? (0.015f + RandFloat() * 0.015f) : (0.028f + RandFloat() * 0.024f);
                float hFrac = RandFloat();
                SpawnBubble(
                    oxygenPos.X + MathF.Cos(ang) * rad + (RandFloat() - 0.5f) * hFrac * 0.32f,
                    oxygenPos.Y + 0.1f + hFrac * (WaterY - oxygenPos.Y - 0.25f),
                    oxygenPos.Z + MathF.Sin(ang) * rad + (RandFloat() - 0.5f) * hFrac * 0.32f,
                    r, 0.72f + r * 18f + RandFloat() * 0.3f, 5.5f);
            }
        }

        // ---------------------------------------------------------------------
        // Procedural Texture Generators
        // ---------------------------------------------------------------------
        static byte[] BuildBackWallBuffer(int w, int h, bool light)
        {
            var buf = new byte[w * h * 4];
            Vector3 cTop = light ? new Vector3(242, 239, 232) : new Vector3(28, 55, 61);
            Vector3 cMid = light ? new Vector3(219, 225, 220) : new Vector3(14, 34, 40);
            Vector3 cBot = light ? new Vector3(167, 184, 180) : new Vector3(6, 17, 21);

            for (int y = 0; y < h; y++)
            {
                float fy = (float)y / Math.Max(1, h - 1);
                Vector3 baseCol = fy < 0.55f
                    ? Vector3.Lerp(cTop, cMid, fy / 0.55f)
                    : Vector3.Lerp(cMid, cBot, (fy - 0.55f) / 0.45f);
                int row = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    float fx = (float)x / Math.Max(1, w - 1);
                    float m1 = MathF.Exp(-((fx - 0.57f) * (fx - 0.57f) / 0.16f + (fy - 0.22f) * (fy - 0.22f) / 0.18f));
                    float m2 = MathF.Exp(-((fx - 0.22f) * (fx - 0.22f) / 0.12f + (fy - 0.72f) * (fy - 0.72f) / 0.14f));
                    Vector3 col = light
                        ? Vector3.Lerp(baseCol, new Vector3(110, 134, 134), m1 * 0.12f + m2 * 0.10f)
                        : Vector3.Lerp(baseCol, new Vector3(110, 178, 172), m1 * 0.16f + m2 * 0.08f);
                    int idx = row + x * 4;
                    buf[idx + 0] = (byte)Math.Clamp(col.Z, 0, 255);
                    buf[idx + 1] = (byte)Math.Clamp(col.Y, 0, 255);
                    buf[idx + 2] = (byte)Math.Clamp(col.X, 0, 255);
                    buf[idx + 3] = 255;
                }
            }
            return buf;
        }

        static uint[] BuildChineseKoiTexture(int size)
        {
            var tex = new uint[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                float belly = MathF.Exp(-MathF.Pow((v - 0.75f) / 0.16f, 2f));
                float r = 252f - belly * 36f;
                float g = 249f - belly * 25f;
                float b = 244f - belly * 18f;

                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float pr = r, pg = g, pb = b;

                    float hi = MathF.Max(
                        EllipseMask(u, v, 0.15f, 0.25f, 0.13f, 0.19f),
                        MathF.Max(
                            EllipseMask(u, v, 0.44f, 0.25f, 0.20f, 0.26f),
                            MathF.Max(
                                EllipseMask(u, v, 0.76f, 0.25f, 0.16f, 0.22f),
                                EllipseMask(u, v, 0.90f, 0.25f, 0.10f, 0.14f))));
                    if (hi > 0f)
                    {
                        float k = MathF.Min(1f, hi * 1.45f);
                        pr = pr * (1f - k) + 232f * k;
                        pg = pg * (1f - k) + 56f * k;
                        pb = pb * (1f - k) + 22f * k;
                    }

                    float sumi = MathF.Max(
                        MathF.Max(EllipseMask(u, v, 0.30f, 0.14f, 0.028f, 0.022f), EllipseMask(u, v, 0.31f, 0.37f, 0.025f, 0.019f)),
                        MathF.Max(EllipseMask(u, v, 0.65f, 0.17f, 0.028f, 0.022f), EllipseMask(u, v, 0.67f, 0.34f, 0.024f, 0.018f)));
                    if (sumi > 0.1f)
                    {
                        float k = MathF.Min(0.88f, sumi * 1.6f);
                        pr *= 1f - k; pg *= 1f - k; pb *= 1f - k;
                    }

                    if (u > 0.20f && belly < 0.75f)
                    {
                        float sx = u * 26f;
                        float sy = v * 26f + ((int)sx % 2) * 0.5f;
                        float fx = sx - MathF.Floor(sx) - 0.5f;
                        float fy = sy - MathF.Floor(sy) - 0.5f;
                        float d = MathF.Sqrt(fx * fx + fy * fy);
                        if (d > 0.36f && d < 0.46f)
                        {
                            float edge = (1f - belly) * 0.18f;
                            pr = Math.Clamp(pr + (fx > 0 ? -36f : 18f) * edge, 0, 255);
                            pg = Math.Clamp(pg + (fx > 0 ? -30f : 16f) * edge, 0, 255);
                            pb = Math.Clamp(pb + (fx > 0 ? -24f : 14f) * edge, 0, 255);
                        }
                    }

                    tex[y * size + x] = 0xFF000000u | ((uint)Math.Clamp((int)pr, 0, 255) << 16) | ((uint)Math.Clamp((int)pg, 0, 255) << 8) | (uint)Math.Clamp((int)pb, 0, 255);
                }
            }
            return tex;
        }

        static uint[] BuildMiniKoiTexture(int size, int variant)
        {
            var tex = new uint[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float r = 252f, g = 249f, b = 242f;
                    if (variant == 0)
                    {
                        float hi = MathF.Max(EllipseMask(u, v, 0.18f, 0.25f, 0.16f, 0.26f),
                            MathF.Max(EllipseMask(u, v, 0.53f, 0.25f, 0.22f, 0.30f), EllipseMask(u, v, 0.85f, 0.25f, 0.14f, 0.22f)));
                        if (hi > 0.05f) { r = 229f; g = 56f; b = 22f; }
                    }
                    else if (variant == 1)
                    {
                        if (v < 0.58f) { r = 242f - u * 16f; g = 82f + u * 58f; b = 24f; }
                    }
                    else
                    {
                        if (EllipseMask(u, v, 0.18f, 0.25f, 0.14f, 0.24f) > 0.05f) { r = 229f; g = 56f; b = 22f; }
                        else if (EllipseMask(u, v, 0.56f, 0.25f, 0.14f, 0.18f) > 0.05f) { r = 30f; g = 34f; b = 40f; }
                    }
                    tex[y * size + x] = 0xFF000000u | ((uint)(int)r << 16) | ((uint)(int)g << 8) | (uint)(int)b;
                }
            }
            return tex;
        }

        static uint[] BuildChineseFinTexture(int size, bool isTail)
        {
            var tex = new uint[size * size];
            for (int y = 0; y < size; y++)
            {
                float fy = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float distFromRoot = 1f - (float)x / size;
                    float r, g, b, a;
                    if (isTail)
                    {
                        r = 222f + distFromRoot * 33f;
                        g = 52f + distFromRoot * 195f;
                        b = 20f + distFromRoot * 226f;
                        a = Math.Clamp(0.95f - distFromRoot * 0.78f, 0.14f, 0.96f);
                    }
                    else
                    {
                        r = 228f + distFromRoot * 27f;
                        g = 68f + distFromRoot * 182f;
                        b = 32f + distFromRoot * 214f;
                        a = Math.Clamp(0.88f - distFromRoot * 0.76f, 0.08f, 0.90f);
                    }

                    float rayWave = MathF.Abs(MathF.Sin((fy - 0.5f) * 34f));
                    if (rayWave > 0.86f)
                    {
                        float k = (1f - distFromRoot * 0.65f) * 0.35f;
                        r *= 1f - k * 0.3f; g *= 1f - k * 0.55f; b *= 1f - k * 0.6f;
                        a = MathF.Min(1f, a + 0.12f * (1f - distFromRoot));
                    }

                    tex[y * size + x] = ((uint)Math.Clamp((int)(a * 255), 0, 255) << 24)
                        | ((uint)Math.Clamp((int)r, 0, 255) << 16)
                        | ((uint)Math.Clamp((int)g, 0, 255) << 8)
                        | (uint)Math.Clamp((int)b, 0, 255);
                }
            }
            return tex;
        }

        static uint[] BuildRockeryTexture(int size)
        {
            var tex = new uint[size * size];
            for (int y = 0; y < size; y++)
            {
                float fy = (float)y / size;
                float br = 90f - fy * 54f;
                float bg = 104f - fy * 60f;
                float bb = 107f - fy * 61f;
                float strata = MathF.Sin(fy * 46f) * 8f + MathF.Sin(fy * 19f) * 6f;
                for (int x = 0; x < size; x++)
                {
                    float fx = (float)x / size;
                    float grain = MathF.Sin(fx * 31f + fy * 23f) * 6f;
                    float moss = fy < 0.45f ? MathF.Max(0f, MathF.Sin(fx * 14f) * MathF.Cos(fy * 16f)) * 0.45f : 0f;
                    float r = br + strata + grain - moss * 26f;
                    float g = bg + strata + grain + moss * 22f;
                    float b = bb + strata + grain - moss * 12f;
                    tex[y * size + x] = 0xFF000000u | ((uint)Math.Clamp((int)r, 0, 255) << 16) | ((uint)Math.Clamp((int)g, 0, 255) << 8) | (uint)Math.Clamp((int)b, 0, 255);
                }
            }
            return tex;
        }

        static uint[] BuildSubstrateTexture(int size)
        {
            var tex = new uint[size * size];
            var rng = new Random(1024);
            for (int i = 0; i < tex.Length; i++)
            {
                int b = 34 + rng.Next(44);
                int warm = rng.Next(100) > 70 ? 10 : 2;
                tex[i] = 0xFF000000u | ((uint)(b + warm) << 16) | ((uint)(b + warm - 1) << 8) | (uint)(b + 4);
            }
            return tex;
        }

        static float EllipseMask(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            float d2 = dx * dx + dy * dy;
            return d2 >= 1f ? 0f : 1f - d2;
        }

        static float RandFloat() => (float)Random.Shared.NextDouble();
        static Vector3 SafeNormalize(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : fallback;

        public void Dispose()
        {
            if (disposed) return;
            lock (syncLock)
            {
                if (disposed) return;
                disposed = true;
                hasPendingPresent = false;
                pixelStream.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // Internal Data Types
        // ---------------------------------------------------------------------
        enum KoiBehavior { Cruise, Feed, Flee }

        sealed class MeshData
        {
            internal Vector3[] Positions = Array.Empty<Vector3>();
            internal Vector3[] Normals = Array.Empty<Vector3>();
            internal Vector2[] Uvs = Array.Empty<Vector2>();
            internal int[] Indices = Array.Empty<int>();

            internal static MeshData Create(Vector3[] pos, Vector2[] uvs, int[] indices)
            {
                var m = new MeshData { Positions = pos, Uvs = uvs, Indices = indices, Normals = new Vector3[pos.Length] };
                m.RecomputeNormals();
                return m;
            }

            internal void RecomputeNormals()
            {
                Array.Clear(Normals);
                for (int i = 0; i < Indices.Length; i += 3)
                {
                    int a = Indices[i], b = Indices[i + 1], c = Indices[i + 2];
                    Vector3 n = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
                    Normals[a] += n; Normals[b] += n; Normals[c] += n;
                }
                for (int i = 0; i < Normals.Length; i++)
                    Normals[i] = Normals[i].LengthSquared() > 1e-8f ? Vector3.Normalize(Normals[i]) : Vector3.UnitY;
            }
        }

        readonly struct StaticMeshInstance(MeshData mesh, Matrix4x4 world, uint[]? texture, int texSize, uint solidColor)
        {
            internal readonly MeshData Mesh = mesh;
            internal readonly Matrix4x4 World = world;
            internal readonly uint[]? Texture = texture;
            internal readonly int TexSize = texSize;
            internal readonly uint SolidColor = solidColor;
        }

        sealed class PlantBlade
        {
            internal required MeshData Mesh;
            internal required Vector3[] OrigPositions;
            internal Matrix4x4 World;
            internal float Height, Phase, SwayAmp;
            internal uint Color;
        }

        sealed class BigKoiState
        {
            internal required MeshData BodyMesh, TailMesh, DorsalMesh, PecLMesh, PecRMesh;
            internal required Vector3[] BodyOrig, TailOrig, DorsalOrig, PecOrig;
            internal float BodyLen, BodyH, TailLen;
            internal Vector3 Pos, Vel, Dir, SmoothDir, FleeTarget, WanderTarget;
            internal KoiBehavior State = KoiBehavior.Cruise;
            internal float FleeEnergy, FleeBurstTimer, WanderTimer, GulpTimer, MouthOpen;
            internal float WagPhase, YawRate, LastYaw, Yaw, Pitch, Roll, BubbleTimer, SwimEffort, BodyBend;
        }

        sealed class SmallKoiState
        {
            internal Vector3 Pos, Vel, Dir, SmoothDir;
            internal float WagPhase, MaxSpeed, Scale;
            internal int Variant;
        }

        struct BubbleState
        {
            internal Vector3 Pos;
            internal float Radius, Vy, Vx, Vz, WobblePhase, WobbleFreq, WobbleAmp, Age, Life;
        }

        sealed class FoodPellet
        {
            internal Vector3 Pos, Vel;
            internal bool InWater, Eaten;
            internal float SinkSpeed, WobblePhase, Radius, Age;
            internal int ColorVariant;
        }

        struct SurfaceRipple
        {
            internal float X, Z, Age, MaxAge, MaxScale, BaseOpacity;
        }

        struct PendingRipple
        {
            internal float X, Z, Delay, MaxScale, Opacity;
        }

        struct SplashDrop
        {
            internal Vector3 Pos, Vel;
            internal float Age;
        }
    }
}