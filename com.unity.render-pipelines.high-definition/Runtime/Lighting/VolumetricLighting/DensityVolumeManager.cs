using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.HighDefinition
{
    class DensityVolumeManager
    {
        public static readonly GraphicsFormat densityVolumeAtlasFormat = GraphicsFormat.R8G8B8A8_UNorm;

        static DensityVolumeManager m_Manager;
        public static DensityVolumeManager manager
        {
            get
            {
                if (m_Manager == null)
                    m_Manager = new DensityVolumeManager();
                return m_Manager;
            }
        }

        Texture3DAtlas m_VolumeAtlas = null;
        public Texture3DAtlas volumeAtlas
        {
            get
            {
                if (m_VolumeAtlas == null)
                {
                    var settings = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.lightLoopSettings;
                    m_VolumeAtlas = new Texture3DAtlas(densityVolumeAtlasFormat, (int)settings.maxDensityVolumeSize, (int)settings.maxDensityVolumesOnScreen);

                    // When HDRP is initialized and this atlas created, some density volume may have been initialized before so we add them here.
                    foreach (var volume in m_Volumes)
                    {
                        if (volume.parameters.volumeMask != null)
                            AddTextureIntoAtlas(volume.parameters.volumeMask);
                    }
                }

                return m_VolumeAtlas;
            }
        }

        List<DensityVolume> m_Volumes = null;

        DensityVolumeManager()
        {
            m_Volumes = new List<DensityVolume>();
        }

        public void RegisterVolume(DensityVolume volume)
        {
            m_Volumes.Add(volume);

            if (volume.parameters.volumeMask != null)
            {
                if (volumeAtlas.IsTextureValid(volume.parameters.volumeMask))
                {
                    AddTextureIntoAtlas(volume.parameters.volumeMask);
                }
            }
        }

        internal void AddTextureIntoAtlas(Texture volumeTexture)
        {
            if (!volumeAtlas.AddTexture(volumeTexture))
                Debug.LogError($"No more space in the density volume atlas, consider increasing the max density volume on screen in the HDRP asset.");
        }

        public void DeRegisterVolume(DensityVolume volume)
        {
            if (m_Volumes.Contains(volume))
                m_Volumes.Remove(volume);

            if (volume.parameters.volumeMask != null)
                volumeAtlas.RemoveTexture(volume.parameters.volumeMask);
        }

        public bool ContainsVolume(DensityVolume volume) => m_Volumes.Contains(volume);

        public List<DensityVolume> PrepareDensityVolumeData(CommandBuffer cmd, HDCamera currentCam, float time)
        {
            //Update volumes
            bool animate = currentCam.animateMaterials;
            foreach (DensityVolume volume in m_Volumes)
                volume.PrepareParameters(animate, time);

            using (new ProfilingScope(cmd, ProfilingSampler.Get(HDProfileId.UpdateDensityVolumeAtlas)))
            {
                volumeAtlas.Update(cmd);
            }

            return m_Volumes;
        }

        // Note that this function will not release the manager itself as it have to live outside of HDRP to handle density volume components
        internal void ReleaseAtlas()
        {
            // Release the atlas so next time the manager is used, it is reallocated with new HDRP settings.
            volumeAtlas.Release();
            m_VolumeAtlas = null;
        }

        // TODO: we'll need to remove texture index
        // private void AtlasUpdated()
        // {
        //     foreach (DensityVolume volume in volumes)
        //     {
        //         volume.parameters.textureIndex = volumeAtlas.GetTextureIndex(volume.parameters.volumeMask);
        //     }
        // }
    }
}
