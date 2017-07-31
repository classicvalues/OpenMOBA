﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using ClipperLib;
using OpenMOBA.Debugging;
using OpenMOBA.DevTool.Debugging;
using OpenMOBA.Geometry;
using OpenMOBA.Foundation;
using OpenMOBA.Foundation.Terrain;
using OpenMOBA.Foundation.Terrain.Snapshots;
using OpenMOBA.Foundation.Visibility;
using Shade;

namespace OpenMOBA.DevTool {
   public static class Program {
      public static void Main(string[] args) {
//         CanvasProgram.EntryPoint(args);
//         return;
         var gameFactory = new GameFactory();
         gameFactory.GameCreated += (s, game) => {
            GameDebugger.AttachToWithSoftwareRendering(game);
         };
         OpenMOBA.Program.Main(gameFactory);
      }
   }

   public class GameDebugger : IGameDebugger {
      private static readonly StrokeStyle PathStroke = new StrokeStyle(Color.Lime, 2.0);
      private static readonly StrokeStyle NoPathStroke = new StrokeStyle(Color.Red, 2.0, new[] { 1.0f, 1.0f });
      private static readonly StrokeStyle HighlightStroke = new StrokeStyle(Color.Red, 3.0);

      public GameDebugger(Game game, IDebugMultiCanvasHost debugMultiCanvasHost) {
         Game = game;
         DebugMultiCanvasHost = debugMultiCanvasHost;
      }

      public Game Game { get; }
      public IDebugMultiCanvasHost DebugMultiCanvasHost { get; }

      private DebugProfiler DebugProfiler => Game.DebugProfiler;
      private GameTimeService GameTimeService => Game.GameTimeService;
      private TerrainService TerrainService => Game.TerrainService;
      private EntityService EntityService => Game.EntityService;

      public void HandleFrameEnd(FrameEndStatistics frameStatistics) {
         if (GameTimeService.Ticks == 0) {
//            AddSquiggleHole();
         }
         if (frameStatistics.EventsProcessed != 0 || GameTimeService.Ticks % 64 == 0) {
            RenderDebugFrame();
         }
      }

      private void AddSquiggleHole() {
         var holeSquiggle = PolylineOperations.ExtrudePolygon(
            new[] {
               new IntVector2(100, 50),
               new IntVector2(100, 100),
               new IntVector2(200, 100),
               new IntVector2(200, 150),
               new IntVector2(200, 200),
               new IntVector2(400, 250),
               new IntVector2(200, 300),
               new IntVector2(400, 315),
               new IntVector2(200, 330),
               new IntVector2(210, 340),
               new IntVector2(220, 350),
               new IntVector2(220, 400),
               new IntVector2(221, 400)
            }.Select(iv => new IntVector2(iv.X + 160, iv.Y + 200)).ToArray(), 10).FlattenToPolygons();
         TerrainService.AddTemporaryHole(new DynamicTerrainHole{ Polygons = holeSquiggle });
      }

      private void RenderDebugFrame() {
         var terrainSnapshot = TerrainService.BuildSnapshot();
         var debugCanvas = DebugMultiCanvasHost.CreateAndAddCanvas(GameTimeService.Ticks);

         var temporaryHolePolygons = terrainSnapshot.TemporaryHoles.SelectMany(th => th.Polygons).ToList();
         var holeDilationRadius = 15.0;

         debugCanvas.BatchDraw(() => {
            foreach (var sectorSnapshot in terrainSnapshot.SectorSnapshots) {
               debugCanvas.Transform = sectorSnapshot.WorldTransform;
               debugCanvas.DrawTriangulation(sectorSnapshot.ComputeTriangulation(holeDilationRadius), new StrokeStyle(Color.DarkGray));

               if (!sectorSnapshot.Sector.EnableDebugHighlight) {
//                  debugCanvas.DrawWallPushGrid(sectorSnapshot, holeDilationRadius);
                  continue;
               }

               var punchedLand = sectorSnapshot.ComputePunchedLand(holeDilationRadius);
               var s = new Stack<PolyNode>();
               punchedLand.Childs.ForEach(s.Push);
               while (s.Any()) {
                  var landNode = s.Pop();
                  foreach (var subLandNode in landNode.Childs.SelectMany(child => child.Childs)) {
                     s.Push(subLandNode);
                  }

                  var visibilityGraph = landNode.ComputeVisibilityGraph();
                  debugCanvas.DrawVisibilityGraph(visibilityGraph);

                  var visibilityGraphNodeData = landNode.visibilityGraphNodeData;
                  if (visibilityGraphNodeData.CrossoverSnapshots == null) {
                     continue;
                  }

                  foreach (var c in visibilityGraphNodeData.CrossoverSnapshots) {
                     debugCanvas.DrawLine(c.LocalSegment.First, c.LocalSegment.Second, new StrokeStyle(Color.Red, 5));
                  }

                  var colors = new[] { Color.Lime, Color.Orange, Color.Cyan, Color.Magenta, Color.Yellow, Color.Pink };
                  for (int crossoverIndex = 0; crossoverIndex < visibilityGraphNodeData.ErodedCrossoverSegments.Count; crossoverIndex++) {
                     var crossover = visibilityGraphNodeData.ErodedCrossoverSegments[crossoverIndex];
                     var destinations = visibilityGraphNodeData.ErodedCrossoverSegments.Select((seg, i) => (seg, i != crossoverIndex)).Where(t => t.Item2)
                                                               .SelectMany(t => t.Item1.Points)
                                                               .Select(visibilityGraph.IndicesByWaypoint.Get)
                                                               .ToArray();
                     var dijkstras = visibilityGraph.Dijkstras(crossover.Points, destinations);
                     for (var i = 0; i < visibilityGraph.Waypoints.Length; i++) {
                        //                     if (double.IsNaN(dijkstras[i].TotalCost)) {
                        //                        continue;
                        //                     }
//                        debugCanvas.DrawText(((int)dijkstras[i].TotalCost).ToString(), visibilityGraph.Waypoints[i]);
//                        debugCanvas.DrawLine(visibilityGraph.Waypoints[i], visibilityGraph.Waypoints[dijkstras[i].PriorIndex], new StrokeStyle(colors[crossoverIndex], 5));
                     }

                     foreach (var vg in landNode.ComputeWaypointVisibilityPolygons()) {
                        //                     debugCanvas.DrawLineOfSight(vg);
                     }

                     var crossoverSeeingWaypoints = landNode.ComputeCrossoverSeeingWaypoints(visibilityGraphNodeData.CrossoverSnapshots[crossoverIndex]);
                     Console.WriteLine(crossoverSeeingWaypoints.Length);
                     foreach (var waypointIndex in crossoverSeeingWaypoints) {
                        debugCanvas.FillPolygon(new[] { visibilityGraph.Waypoints[waypointIndex], crossover.First, crossover.Second }, new FillStyle(Color.FromArgb(150, colors[crossoverIndex])));
                        debugCanvas.DrawLine(visibilityGraph.Waypoints[waypointIndex], crossover.First, new StrokeStyle(colors[crossoverIndex], 5));
                        debugCanvas.DrawLine(visibilityGraph.Waypoints[waypointIndex], crossover.Second, new StrokeStyle(colors[crossoverIndex], 5));
                     }
                  }
               }
            }

//            foreach (var sectorSnapshot in terrainSnapshot.SectorSnapshots) {
//               debugCanvas.Transform = sectorSnapshot.WorldTransform;
//
//               var s = new Stack<PolyNode>();
//               sectorSnapshot.ComputePunchedLand(holeDilationRadius).Childs.ForEach(s.Push);
//               while (s.Any()) {
//                  var landNode = s.Pop();
//                  foreach (var subLandNode in landNode.Childs.SelectMany(child => child.Childs)) {
//                     s.Push(subLandNode);
//                  }
//
//                  if (landNode.visibilityGraphNodeData.CrossoverSnapshots == null) {
//                     continue;
//                  }
//                  foreach (var crossover in landNode.visibilityGraphNodeData.CrossoverSnapshots) {
//                     debugCanvas.DrawLine(crossover.LocalSegment.First, crossover.LocalSegment.Second, new StrokeStyle(Color.Red, 5));
//                  }
//               }
//            }

//            debugCanvas.Transform = Matrix4x4.Identity;
//            DrawTestPathfindingQueries(debugCanvas, holeDilationRadius);
//            DrawEntities(debugCanvas);
         });
      }

      private void DrawEntityPaths(IDebugCanvas debugCanvas) {
         foreach (var entity in EntityService.EnumerateEntities()) {
            var movementComponent = entity.MovementComponent;
            if (movementComponent == null) continue;
            var pathPoints = new[] { movementComponent.Position }.Concat(entity.MovementComponent.PathingBreadcrumbs).ToList();
            debugCanvas.DrawLineStrip(pathPoints, PathStroke);
         }
      }

      private void DrawEntities(IDebugCanvas debugCanvas) {
         foreach (var entity in EntityService.EnumerateEntities()) {
            var movementComponent = entity.MovementComponent;
            if (movementComponent != null) {
               debugCanvas.DrawPoint(movementComponent.Position, new StrokeStyle(Color.Black, 2 * movementComponent.BaseRadius));
               debugCanvas.DrawPoint(movementComponent.Position, new StrokeStyle(Color.White, 2 * movementComponent.BaseRadius - 2));

               if (movementComponent.Swarm != null && movementComponent.WeightedSumNBodyForces.Norm2D() > GeometryOperations.kEpsilon) {
                  var direction = movementComponent.WeightedSumNBodyForces.ToUnit() * movementComponent.BaseRadius;
                  var to = movementComponent.Position + new DoubleVector3(direction.X, direction.Y, 0.0);
                  debugCanvas.DrawLine(movementComponent.Position, to, new StrokeStyle(Color.Gray));
               }

               if (movementComponent.DebugLines != null) {
                  debugCanvas.DrawLineList(
                     movementComponent.DebugLines.SelectMany(pair => new[] { pair.Item1, pair.Item2 }).ToList(),
                     new StrokeStyle(Color.Black));
               }
            }
         }
      }

      private void DrawHighlightedEntityTriangles(SectorSnapshot sectorSnapshot, DebugCanvas debugCanvas) {
         foreach (var entity in EntityService.EnumerateEntities()) {
            var movementComponent = entity.MovementComponent;
            if (movementComponent != null) {
               var triangulation = sectorSnapshot.ComputeTriangulation(movementComponent.BaseRadius);
               TriangulationIsland island;
               int triangleIndex;
               if (triangulation.TryIntersect(movementComponent.Position.X, movementComponent.Position.Y, out island, out triangleIndex)) {
                  debugCanvas.DrawTriangle(island.Triangles[triangleIndex], HighlightStroke);
               }
            }
         }
      }

      private void DrawTestPathfindingQueries(IDebugCanvas debugCanvas, double holeDilationRadius) {
         var testPathFindingQueries = new[] {
            Tuple.Create(new DoubleVector3(60, 40, 0), new DoubleVector3(930, 300, 0)),
            Tuple.Create(new DoubleVector3(675, 175, 0), new DoubleVector3(825, 300, 0)),
            Tuple.Create(new DoubleVector3(50, 900, 0), new DoubleVector3(950, 475, 0)),
            Tuple.Create(new DoubleVector3(50, 500, 0), new DoubleVector3(80, 720, 0))
         };

         // scale 90%, above points are for [0,0] to [1000, 1000] but demo is now [0,0] to [900,900].
         for (var i = 0; i < testPathFindingQueries.Length; i++) {
            testPathFindingQueries[i] = new Tuple<DoubleVector3, DoubleVector3>(
               new DoubleVector3(
                  testPathFindingQueries[i].Item1.X * 0.9,
                  testPathFindingQueries[i].Item1.Y * 0.9,
                  testPathFindingQueries[i].Item1.Z * 0.9),
               new DoubleVector3(
                  testPathFindingQueries[i].Item2.X * 0.9,
                  testPathFindingQueries[i].Item2.Y * 0.9,
                  testPathFindingQueries[i].Item2.Z * 0.9)
            );
         }

         var sector1 = Game.TerrainService.BuildSnapshot().SectorSnapshots[1];
         var p1 = new IntVector2(500, 300);
         var p1World = Vector3.Transform(new DoubleVector3(p1.ToDoubleVector2()).ToDotNetVector(), sector1.WorldTransform).ToOpenMobaVector();
         sector1.ComputePunchedLand(holeDilationRadius).PickDeepestPolynode(p1, out PolyNode p1PolyNode, out bool p1IsInHole);
         debugCanvas.DrawPoint(p1World, new StrokeStyle(p1IsInHole ? Color.Green : Color.Lime, 20));

         var sector2 = Game.TerrainService.BuildSnapshot().SectorSnapshots[2];
         var p2 = new IntVector2(350, 320);
         var p2World = Vector3.Transform(new DoubleVector3(p2.ToDoubleVector2()).ToDotNetVector(), sector2.WorldTransform).ToOpenMobaVector();
         sector2.ComputePunchedLand(holeDilationRadius).PickDeepestPolynode(p2, out PolyNode p2PolyNode, out bool p2IsInHole);
         debugCanvas.DrawPoint(p2World, new StrokeStyle(p2IsInHole ? Color.DarkRed : Color.Red, 20));

         double pathCostUpperBound;
         if (Game.PathfinderCalculator.TryFindPathCostUpperBound(holeDilationRadius, p1, p1PolyNode, p2, p2PolyNode, out pathCostUpperBound)) {
            debugCanvas.DrawLine(p1World, p2World, PathStroke);
         } else {
            debugCanvas.DrawLine(p1World, p2World, NoPathStroke);
         }

         //         foreach (var query in testPathFindingQueries) {
         //            Game.PathfinderCalculator.TryFindPathCostUpperBound(holeDilationRadius)
         //            List<DoubleVector3> pathPoints;
         //            if (Game.PathfinderCalculator.TryFindPath(holeDilationRadius, query.Item1, query.Item2, out pathPoints)) {
         //               debugCanvas.DrawLineStrip(pathPoints, PathStroke);
         //            }
         //         }
      }

      public static void AttachToWithSoftwareRendering(Game game) {
         var rotation = 80 * Math.PI / 180.0;
         float scale = 1.0f;
         var displaySize = new Size((int)(1400 * scale), (int)(700 * scale));
         var projector = new PerspectiveProjector(
            new DoubleVector3(500, 500, 0) + DoubleVector3.FromRadiusAngleAroundXAxis(500, rotation),
            new DoubleVector3(500, 500, 0),
            DoubleVector3.FromRadiusAngleAroundXAxis(1, rotation + Math.PI / 2),
            displaySize.Width,
            displaySize.Height);
         //         projector = null;
         //         var debugMultiCanvasHost = new MonoGameCanvasHost();
         var debugMultiCanvasHost = Debugging.DebugMultiCanvasHost.CreateAndShowCanvas(
            displaySize,
            new Point(100, 100),
            projector);
         AttachTo(game, debugMultiCanvasHost);
      }

      public static void AttachTo(Game game, IDebugMultiCanvasHost debugMultiCanvasHost) {
         var debugger = new GameDebugger(game, debugMultiCanvasHost);
         game.Debuggers.Add(debugger);
      }
   }
}
