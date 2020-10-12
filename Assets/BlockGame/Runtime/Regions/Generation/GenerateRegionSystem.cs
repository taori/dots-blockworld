﻿using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Sark.Common;

using static Sark.Common.GridUtil;

namespace Sark.BlockGame
{
    public struct GenerateRegion : IComponentData {}
    public struct RegionGenerated : IComponentData {}


    public class GenerateRegionSystem : SystemBase
    {
        EndSimulationEntityCommandBufferSystem _endSimBarrier;
        EntityQuery _regionsToGenerate;

        MapGenSettingsAsset _genSettings;

        protected override void OnCreate()
        {
            _regionsToGenerate = GetEntityQuery(
                ComponentType.ReadOnly<Region>(),
                ComponentType.ReadOnly<GenerateRegion>(),
                ComponentType.Exclude<HeightMap>()
                );

            _endSimBarrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
            _genSettings = Resources.Load<MapGenSettingsAsset>("MapGenSettings");
        }

        protected override void OnUpdate()
        {
            var ecb = _endSimBarrier.CreateCommandBuffer().AsParallelWriter();

            int chunkSize = 16;

            var genSettings = _genSettings.Settings;
            var chunkPrefab = World.GetOrCreateSystem<VoxelWorldSystem>().GetVoxelChunkPrefab();

            Entities
            .WithName("GenerateHeightMap")
            .WithNone<HeightMap>()
            .WithAll<GenerateRegion>()
            .ForEach((int entityInQueryIndex, Entity e, in Region region) =>
            {
                var map = ecb.AddBuffer<HeightMap>(entityInQueryIndex, e);
                map.ResizeUninitialized(chunkSize * chunkSize);
                var arr = map.Reinterpret<ushort>().AsNativeArray();

                int2 regionIndex = region.Index;
                int2 origin = regionIndex * chunkSize;

                BuildMap(
                    arr,
                    chunkSize,
                    origin,
                    genSettings,
                    out int highest);

                if (highest == 0)
                    return;

                int maxChunkHeight = highest / Grid3D.CellSizeY;

                for(int chunkIndexY = 0; chunkIndexY <= maxChunkHeight; ++chunkIndexY )
                {
                    var chunkIndex = new int3(regionIndex.x, chunkIndexY, regionIndex.y);
                    var chunk = ecb.Instantiate(entityInQueryIndex, chunkPrefab);

                    ecb.SetComponent(entityInQueryIndex, chunk, new VoxelChunk
                    {
                        Index = chunkIndex,
                        Region = e
                    });

                    ecb.AppendToBuffer<LinkedEntityGroup>(entityInQueryIndex, e, chunk);
                    ecb.AddComponent<GenerateChunk>(entityInQueryIndex, chunk);
                }
            }).ScheduleParallel();

            _endSimBarrier.AddJobHandleForProducer(Dependency);
        }

        static void BuildMap(
            NativeArray<ushort> map, 
            int size, 
            int2 origin,
            MapGenSettings settings,
            out int highest)
        {
            highest = int.MinValue;
            for( int i = 0; i < map.Length; ++i )
            {
                int2 xz = Grid2D.IndexToPos(i);

                int2 p = origin + xz;
                float noise = NoiseUtil.SumOctave(p.x, p.y,
                    settings.Iterations,
                    settings.Persistance,
                    settings.Scale,
                    settings.Low,
                    settings.High);

                map[i] = (ushort)math.floor(noise);
                highest = math.max(highest, map[i]);
            }
        }
    } 
}
