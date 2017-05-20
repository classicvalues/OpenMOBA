﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;

namespace OpenMOBA.DevTool.Debugging.Canvas3D {
   // column-major matrices
   public static class MatrixCM {
      public static Matrix LookAtRH(Vector3 eye, Vector3 target, Vector3 up) {
         var matrix = Matrix.LookAtRH(eye, target, up);
         matrix.Transpose();
         return matrix;
      }

      public static Matrix PerspectiveFovRH(float fov, float aspect, float znear, float zfar) {
         var matrix = Matrix.PerspectiveFovRH(fov, aspect, znear, zfar);
         matrix.Transpose();
         return matrix;
      }

      public static Matrix Scaling(float scale) {
         var matrix = Matrix.Scaling(scale);
         matrix.Transpose();
         return matrix;
      }

      public static Matrix Translation(float x, float y, float z) {
         var matrix = Matrix.Translation(x, y, z);
         matrix.Transpose();
         return matrix;
      }

      public static Matrix RotationX(float rads) {
         var matrix = Matrix.RotationX(rads);
         matrix.Transpose();
         return matrix;
      }

      public static Matrix RotationY(float rads) {
         var matrix = Matrix.RotationY(rads);
         matrix.Transpose();
         return matrix;
      }

      public static Matrix RotationZ(float rads) {
         var matrix = Matrix.RotationZ(rads);
         matrix.Transpose();
         return matrix;
      }
   }
}
