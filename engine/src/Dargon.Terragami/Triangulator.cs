﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Dargon.Commons.Exceptions;
using Dargon.PlayOn.DataStructures;
using Dargon.PlayOn.Geometry;
using Dargon.Terragami;
using Dargon.Terragami.ThirdParty.ClipperLib;
using Poly2Tri;
using Poly2Tri.Triangulation.Polygon;
//using Polygon = Poly2Tri.Polygon;

using cInt = System.Int32;

namespace Dargon.PlayOn {
   // Connected Components. We avoid that term because 'Component' is pretty overloaded in meaning.
   public class TriangulationIsland {
      public Triangle3[] Triangles { get; set; }
      public IntRect2 IntBounds { get; set; }
      public QuadTree<int> TriangleIndexQuadTree { get; set; }

#if use_fixed
      public AxisAlignedBoundingBox2[] FixedOptimizationTriangleBounds { get; set; }
#endif
   }

   public class Triangulation {
      public List<TriangulationIsland> Islands { get; set; }
   }

   public struct Triangle3 {
      public const int NO_NEIGHBOR_INDEX = -1;

      public int Index;

      // in counterclockwise order
      public Array3<DoubleVector2> Points;

      public DoubleVector2 Centroid;

      // also in counterclockwise order, NO_NEIGHBOR_INDEX indicates no neighbor
      public Array3<int> NeighborOppositePointIndices; // triangle index
      public Array3<int> NeighborVertexIndexSharingEdgeOppositePointIndices;

      public IntRect2 IntPaddedBounds2D;

      public override string ToString() => $"[{Points.A} {Points.B} {Points.C}]";
   }

   public struct Array3<T> : IReadOnlyList<T> {
      public Array3(T a, T b, T c) {
         A = a;
         B = b;
         C = c;
      }

      public T A;
      public T B;
      public T C;

      public int Count => 3;

      public T this[int index]
      {
         get
         {
            if (index == 0) return A;
            else if (index == 1) return B;
            else if (index == 2) return C;
            else throw new IndexOutOfRangeException();
         }
         set
         {
            if (index == 0) A = value;
            else if (index == 1) B = value;
            else if (index == 2) C = value;
            else throw new IndexOutOfRangeException();
         }
      }

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

      public IEnumerator<T> GetEnumerator() {
         yield return A;
         yield return B;
         yield return C;
      }
   }

   public class Triangulator {
      public Triangulation TriangulateRoot(PolygonNode polyTree) {
         if (!polyTree.IsHole || polyTree.Contour != null) {
            throw new ArgumentException("Expected polytree to be contourless root hole!");
         }
         var islands = new List<TriangulationIsland>();
         foreach (var child in polyTree.Children) {
            TriangulateHelper(child, islands);
         }
         return new Triangulation { Islands = islands };
      }

      public Triangulation TriangulateLandNode(PolygonNode landNode) {
         if (landNode.IsHole || !landNode.Contour.Any() || landNode.Parent == null) {
            throw new ArgumentException("Expected polynode to be contourful non-root hole!");
         }
         var islands = new List<TriangulationIsland>();
         TriangulateHelper(landNode, islands);
         return new Triangulation { Islands = islands };
      }

      private void TriangulateHelper(PolygonNode node, List<TriangulationIsland> islands) {
         DebugPrint("TriangulateRoot out");

         var outer = ConvertToTriangulationPoints(node.Contour);
         outer.Reverse();
         var cps = new Polygon(outer);
         foreach (var hole in node.Children) {
            var inner = ConvertToTriangulationPoints(hole.Contour);
            inner.Reverse();
            cps.AddHole(new Polygon(inner));
         }
         P2T.Triangulate(cps);

         var triangles = new Triangle3[cps.Triangles.Count];
         var p2tTriangleToIndex = new Dictionary<Poly2Tri.Triangulation.Delaunay.DelaunayTriangle, int>();
         for (int i = 0; i < cps.Triangles.Count; i++) {
            triangles[i].Index = i;
            p2tTriangleToIndex[cps.Triangles[i]] = i;
         }

         for (var i = 0; i < cps.Triangles.Count; i++) {
            var p2tTriangle = cps.Triangles[i];
            ref Triangle3 t = ref triangles[i];
            t.Points = new Array3<DoubleVector2>(
               p2tTriangle.Points[0].ToOpenMobaPointD(),
               p2tTriangle.Points[1].ToOpenMobaPointD(),
               p2tTriangle.Points[2].ToOpenMobaPointD()
            );
//            Console.WriteLine(string.Join(", ", p2tTriangle.Points.Select(p => p.Z)));
            t.Centroid = (t.Points[0] + t.Points[1] + t.Points[2]) / CDoubleMath.c3;
            t.IntPaddedBounds2D = CreatePaddedIntAxisAlignedBoundingBoxXY2D(ref triangles[i].Points);
            for (int j = 0; j < 3; j++) {
               var neighbor = p2tTriangle.Neighbors[j];
               if (neighbor != null && neighbor.IsInterior) {
                  triangles[i].NeighborOppositePointIndices[j] = p2tTriangleToIndex[neighbor];

                  // technically only need to check 1 vert assuming always same relative clockness order
                  // Note: The shared edge is ordered opposite between two neighboring triangles.
                  //   A______D => ABC, BAD
                  //   /\    /
                  //  /  \  /
                  // /____\/
                  // C     B
                  var e0 = p2tTriangle.Points[j >= 2 ? j - 2 : j + 1];
                  var e1 = p2tTriangle.Points[j >= 1 ? j - 1 : j + 2];
                  triangles[i].NeighborVertexIndexSharingEdgeOppositePointIndices[j] =
                     neighbor.Points[0].Equals(e1) && neighbor.Points[1].Equals(e0) ? 2 :
                        neighbor.Points[1].Equals(e1) && neighbor.Points[2].Equals(e0) ? 0 :
                           neighbor.Points[2].Equals(e1) && neighbor.Points[0].Equals(e0) ? 1 :
                              throw new InvalidStateException();

                  // todo: can populate other triangle node here too via symmetry optimization
               } else {
                  triangles[i].NeighborOppositePointIndices[j] = Triangle3.NO_NEIGHBOR_INDEX;
                  triangles[i].NeighborVertexIndexSharingEdgeOppositePointIndices[j] = Triangle3.NO_NEIGHBOR_INDEX;
               }

#if DEBUG
               var p0 = triangles[i].Points[0];
               var p1 = triangles[i].Points[1];
               var p2 = triangles[i].Points[2];
               var centroid = triangles[i].Centroid;
               var cp0 = p0 - centroid;
               var cp1 = p1 - centroid;
               var cp2 = p2 - centroid;
               var cl01 = GeometryOperations.Clockness(cp0, cp1);
               var cl12 = GeometryOperations.Clockness(cp1, cp2);
               var cl20 = GeometryOperations.Clockness(cp2, cp0);
               var cond = cl01 == cl12 && cl12 == cl20 && cl20 == Clockness.ClockWise;
               if (!cond) {
                  throw new ArgumentException("P2T Triangle Clockness wasn't expected");
               }
#endif
            }
         }
         var islandBoundingBox = new IntRect2 {
            Left = triangles.Min(t => t.IntPaddedBounds2D.Left),
            Top = triangles.Min(t => t.IntPaddedBounds2D.Top),
            Right = triangles.Max(t => t.IntPaddedBounds2D.Right),
            Bottom = triangles.Max(t => t.IntPaddedBounds2D.Bottom)
         };
         var triangleIndexQuadTree = new QuadTree<int>(8, 8, islandBoundingBox);
         for (var i = 0; i < triangles.Length; i++) {
            triangleIndexQuadTree.Insert(i, triangles[i].IntPaddedBounds2D);
         }
         islands.Add(new TriangulationIsland {
            Triangles = triangles,
            IntBounds = islandBoundingBox,
            TriangleIndexQuadTree = triangleIndexQuadTree,
#if use_fixed
            FixedOptimizationTriangleBounds = triangles.Map(t => AxisAlignedBoundingBox2.BoundingPoints(new []{ t.Points[0], t.Points[1], t.Points[2] })),
#endif
         });
         foreach (var innerChild in node.Children.SelectMany(hole => hole.Children)) {
            TriangulateHelper(innerChild, islands);
         }
      }

      private IntRect2 CreatePaddedIntAxisAlignedBoundingBoxXY2D(ref Array3<DoubleVector2> points) {
         cInt minX = cInt.MaxValue;
         cInt maxX = cInt.MinValue;
         cInt minY = cInt.MaxValue;
         cInt maxY = cInt.MinValue;
         for (int i = 0; i < points.Count; i++) {
            minX = Math.Min(minX, (cInt)CDoubleMath.Floor(points[i].X) - 1);
            maxX = Math.Max(maxX, (cInt)CDoubleMath.Ceiling(points[i].X) + 1);
            minY = Math.Min(minY, (cInt)CDoubleMath.Floor(points[i].Y) - 1);
            maxY = Math.Max(maxY, (cInt)CDoubleMath.Ceiling(points[i].Y) + 1);
         }
         return new IntRect2 {
            Left = minX,
            Top = minY,
            Right = maxX,
            Bottom = maxY
         };
      }

      private List<PolygonPoint> ConvertToTriangulationPoints(IntVector2[] points) {
         var isOpen = points[0] != points[points.Length - 1];
         var results = new List<PolygonPoint>(points.Length);
         var limit = isOpen ? points.Length : points.Length - 1;
         for (var i = 0; i < limit; i++) {
            var p = points[i];
            DebugPrint($"P: {p.X}, {p.Y}");
            results.Add(new PolygonPoint(p.X, p.Y));
         }
         DebugPrint($"Last: {points.Last().X}, {points.Last().Y}");
         return results;
      }

      private void DebugPrint(object s) {
         //         Console.WriteLine(s);
      }
   }
}
