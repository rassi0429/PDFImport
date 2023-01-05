using BaseX;
using FrooxEngine;
using HarmonyLib;
using NeosModLoader;
using ImageMagick;
using HarmonyLib;
using NeosModLoader;
using FrooxEngine;
using System.Collections.Generic;
using System;
using CodeX;
using BaseX;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PDFImport
{
    public class PDFImport : NeosMod
    {
        public override string Name => "PDFImport";
        public override string Author => "kka429";
        public override string Version => "0.0.1";
        public override string Link => "";


        public override void OnEngineInit()
        {
            var harmony = new Harmony("com.kokoa.PDFImport");
            harmony.PatchAll();
        }
    }


    [HarmonyPatch(typeof(UniversalImporter), "ImportTask", new Type[] { typeof(AssetClass), typeof(IEnumerable<string>), typeof(World), typeof(float3), typeof(floatQ), typeof(float3), typeof(bool) })]
    class Patch
    {

        static bool Prefix(
              AssetClass assetClass,
              IEnumerable<string> files,
              World world,
              float3 position,
              floatQ rotation,
              float3 scale,
              bool silent = false)
        {
            if (assetClass == AssetClass.Document)
            {
                foreach (string file in files)
                {
                    if (file.ToLower().EndsWith("pdf"))
                    {
                        Slot targetSlot = world.LocalUserSpace.AddSlot(Path.GetFileName(file));
                        targetSlot.GlobalPosition = position;
                        targetSlot.GlobalRotation = rotation;
                        targetSlot.GlobalScale = scale;

                        UniversalImporter.UndoableImport(targetSlot, (Func<Task>)(() =>
                            PrefixAsync(assetClass, files, world, position, rotation, scale, false, targetSlot, file)));
                        return false;
                    }
                }
            }
            return true;
        }


        static async Task<bool> PrefixAsync(
              AssetClass assetClass,
              IEnumerable<string> files,
              World world,
              float3 position,
              floatQ rotation,
              float3 scale,
              bool silent,
              Slot targetSlot,
              string file)
        {


            Slot slot1 = world.AddSlot("Import indicator");
            slot1.PersistentSelf = false;
            NeosLogoMenuProgress logoMenuProgress = slot1.AttachComponent<NeosLogoMenuProgress>();
            logoMenuProgress.Spawn(targetSlot.GlobalPosition, 0.05f, true);
            logoMenuProgress.UpdateProgress(-1f, "Waiting to PDF import", "");

            await new ToBackground();

            PDFImport.Msg("1");

            var settings = new MagickReadSettings();
            settings.Density = new Density(200, 200);
            // settings.BackgroundColor = new MagickColor(65535, 65535, 65535);

            LocalDB localDb = targetSlot.World.Engine.LocalDB;

            await new ToWorld();
            using (var images = new MagickImageCollection())
            {
                await new ToBackground();
                images.Read(file, settings);

                await new ToWorld();
                var page = 1;
                foreach (var image in images)
                {
                    //string tempFilePath1 = localDb.GetTempFilePath("png");
                    // Write page to file that contains the page number


                    await Task.Run(() => ImportImage(targetSlot, localDb, page, image));

                    page++;

                    logoMenuProgress.UpdateProgress(-1f, "Waiting to PDF import", $"page: {page}");
                }
            }

            logoMenuProgress.ProgressDone("Done!");

            return false;
        }

        private static async Task ImportImage(Slot targetSlot, LocalDB localDb, int page, IMagickImage<ushort> image)
        {
            await new ToBackground();
            string signature = LocalDB.GenerateGUIDSignature();
            Uri url = localDb.GenerateLocalURL("png", signature);

            string str = Path.Combine(localDb.AssetStoragePath, signature + ".png");
            FileUtil.Delete(str);

            image.Alpha(AlphaOption.Remove);
            image.Write(str);

            var uri = await localDb.ImportLocalAssetAsync(str, LocalDB.ImportLocation.Move);

            await new ToWorld();

            Slot child = targetSlot.AddSlot($"page{page}");
            StaticTexture2D tex = child.AttachComponent<StaticTexture2D>();
            tex.URL.Value = uri;

            ImageImporter.SetupTextureProxyComponents(child, (IAssetProvider<Texture2D>)tex, StereoLayout.None, ImageProjection.Perspective, false);

            //while (!tex.IsAssetAvailable)
            //    await new NextUpdate();
            CreateQuad(child, (IAssetProvider<ITexture2D>)tex, StereoLayout.None, true, new float2(image.Width, image.Height));

            child.AttachComponent<Grabbable>().Scalable.Value = true;
        }

        private static void CreateQuad(
          Slot slot,
          IAssetProvider<ITexture2D> texture,
          StereoLayout layout,
          bool addCollider,
          float2 resolution)
        {

            UnlitMaterial mat = slot.AttachComponent<UnlitMaterial>();
            QuadMesh quadMesh = slot.AttachComponent<QuadMesh>();
            MeshRenderer meshRenderer = slot.AttachComponent<MeshRenderer>();
            meshRenderer.Mesh.Target = (IAssetProvider<Mesh>)quadMesh;
            meshRenderer.Material.Target = (IAssetProvider<Material>)mat;
            mat.Texture.Target = texture;
            mat.Sidedness.Value = Sidedness.Double;
            ImageImporter.SetupStereoLayout((IStereoMaterial)mat, layout);
            mat.BlendMode.Value = BlendMode.Alpha;
            if ((texture is StaticTexture2D staticTexture2D ? (staticTexture2D.IsNormalMap.Value ? 1 : 0) : 0) != 0)
                mat.DecodeAsNormalMap.Value = true;
            Sync<float2> size = quadMesh.Size;
            float2 normalized = resolution.Normalized;
            float2 float2_1 = ImageImporter.StereoLayoutScaleRatio(layout);
            float2 float2_2 = normalized * float2_1;
            size.Value = float2_2;
            if (addCollider)
            {
                BoxCollider boxCollider = slot.AttachComponent<BoxCollider>();
                boxCollider.Size.DriveFromXY((IField<float2>)quadMesh.Size);
                boxCollider.Type.Value = ColliderType.NoCollision;
            }
            Texture2D tex2D = texture.Asset as Texture2D;
            if (tex2D == null)
                return;
            Action action;
            slot.World.Coroutines.StartBackgroundTask((Func<Task>)(async () =>
            {
                if ((await tex2D.GetOriginalTextureData().ConfigureAwait(false)).HasTransparentPixels())
                    return;
                mat.RunSynchronously((action = (Action)(() => mat.BlendMode.Value = BlendMode.Opaque)), false);
            }));
        }
    }
}