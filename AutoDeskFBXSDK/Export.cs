﻿namespace AutoDeskFBXSDK
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using ACHF;

    using ACSG;

    using ArcManagedFBX;
    using System.Diagnostics;
    using ArcManagedFBX.Layers;

    public class Exporter
    {
        private readonly CHFGeneral helperFunctionsGeneral = new CHFGeneral();

        private readonly Dictionary<string, int> textureIdList = new Dictionary<string, int>();

        private readonly List<FBXTexture> textureList = new List<FBXTexture>();

        private string[] uniqueNamesList = new string[0];

        private int uniqueNamesListCount;

        public void Export(string fileName, CSG sceneGraph, CSGGroup group)
        {
            Common.InitializeSdkObjects(group.Name, out FBXManager managerInstance, out FBXScene fbxScene);

            FBXAnimStack myAnimStack = FBXAnimStack.Create(fbxScene, group.Name + " Animation Stack");
            FBXAnimLayer myAnimBaseLayer = FBXAnimLayer.Create(fbxScene, "Layer0");
            myAnimStack.AddMember(myAnimBaseLayer);

            var node = fbxScene.GetRootNode();

            this.ExportGroupRecursively(group, fbxScene, node, myAnimBaseLayer, sceneGraph.ApplicationState.SettingAnimationKeyFramesPerSecond);

            int fileFormat = -1;
            if (string.Equals(Path.GetExtension(fileName), ".fbx", StringComparison.InvariantCultureIgnoreCase))
            {
                fileFormat = 1; // force text export for fbx
            }

            Common.SaveScene(managerInstance, fbxScene, fileName, fileFormat);
        }

        private static CSGMatrix ConvertTransformToRightHanded_ThisLittleBitTookFourDaysToFigureOut(CSGMatrix transform)
        {
            // mirror each base vector's z component
            transform.m13 *= -1;
            transform.m23 *= -1;
            transform.m33 *= -1;
            transform.m43 *= -1;

            // and now invert the Z base vector to keep the matrix decomposable
            // will come out just fine when the meshes are locally mirrored as well
            transform.m31 *= -1;
            transform.m32 *= -1;
            transform.m33 *= -1; // NOTE: this is inverted twice. Not sure why, but if there is a bug, this is probably it.
            transform.m34 *= -1;
            return transform;
        }

        private void ExportGroupRecursively(CSGGroup group, FBXScene fbxScene, FBXNode sceneNode, FBXAnimLayer animationLayer, float keyFramesPerSecond)
        {
            string groupName = group.Name;
            if (string.IsNullOrWhiteSpace(groupName))
            {
                groupName = "group";
            }

            this.helperFunctionsGeneral.MakeNameUnique(ref groupName, ref this.uniqueNamesList, ref this.uniqueNamesListCount);

            var node = FBXNode.Create(sceneNode, groupName);
            sceneNode.AddChild(node);

            this.SetTransform(group, node);

            CSGAnimationKey[] animationKeyList = null;
            int keyListCount = 0;
            group.GetAnimationKeys(0, 999999, ref animationKeyList, ref keyListCount);
            int translationAnimationKeyCount = 0;
            int rotationAnimationKeyCount = 0;
            foreach (var animationKey in animationKeyList)
            {
                if (animationKey.Type == CSGAnimationKeyType.CSGAnimationPositionKey)
                {
                    translationAnimationKeyCount++;
                }
                if (animationKey.Type == CSGAnimationKeyType.CSGAnimationOrientationKey)
                {
                    rotationAnimationKeyCount++;
                }
            }

            if (translationAnimationKeyCount > 1)
            {
                FBXAnimCurveNode myTranslationAnimCurveNode = node.LclTranslationGetCurveNode(animationLayer);

                FBXAnimCurve myTranXCurve = node.LclTranslationGetCurve(animationLayer, "X");
                FBXAnimCurve myTranYCurve = node.LclTranslationGetCurve(animationLayer, "Y");
                FBXAnimCurve myTranZCurve = node.LclTranslationGetCurve(animationLayer, "Z");

                myTranXCurve.KeyModifyBegin();
                myTranYCurve.KeyModifyBegin();
                myTranZCurve.KeyModifyBegin();

                foreach (var animationKey in animationKeyList)
                {
                    if (animationKey.Type == CSGAnimationKeyType.CSGAnimationPositionKey)
                    {
                        myTranXCurve.KeyAddSet(animationKey.Time / keyFramesPerSecond, animationKey.V.X); //, ArcManagedFBX.Types.EInterpolationType.eInterpolationConstant);
                        myTranYCurve.KeyAddSet(animationKey.Time / keyFramesPerSecond, animationKey.V.Y); //, ArcManagedFBX.Types.EInterpolationType.eInterpolationConstant);
                        myTranZCurve.KeyAddSet(animationKey.Time / keyFramesPerSecond, -animationKey.V.Z); //, ArcManagedFBX.Types.EInterpolationType.eInterpolationConstant);
                    }
                }

                myTranXCurve.KeyModifyEnd();
                myTranYCurve.KeyModifyEnd();
                myTranZCurve.KeyModifyEnd();
            }

            if (rotationAnimationKeyCount > 1)
            {
                FBXAnimCurveNode myRotationAnimCurveNode = node.LclRotationGetCurveNode(animationLayer);

                FBXAnimCurve myRotXCurve = node.LclRotationGetCurve(animationLayer, "X");
                FBXAnimCurve myRotYCurve = node.LclRotationGetCurve(animationLayer, "Y");
                FBXAnimCurve myRotZCurve = node.LclRotationGetCurve(animationLayer, "Z");

                myRotXCurve.KeyModifyBegin();
                myRotYCurve.KeyModifyBegin();
                myRotZCurve.KeyModifyBegin();

                foreach (var animationKey in animationKeyList)
                {
                    if (animationKey.Type == CSGAnimationKeyType.CSGAnimationOrientationKey)
                    {
                        FBXVector eulerXYZ = this.GetEulerXYZ(group, animationKey.Time);
                        //Debug.WriteLine(string.Format("(0):{1},{2},{3}", animationKey.Time, eulerXYZ.x, eulerXYZ.y, eulerXYZ.z));
                        myRotXCurve.KeyAddSet(animationKey.Time / keyFramesPerSecond, (float)eulerXYZ.x); //, ArcManagedFBX.Types.EInterpolationType.eInterpolationLinear);
                        myRotYCurve.KeyAddSet(animationKey.Time / keyFramesPerSecond, (float)eulerXYZ.y); //, ArcManagedFBX.Types.EInterpolationType.eInterpolationLinear);
                        myRotZCurve.KeyAddSet(animationKey.Time / keyFramesPerSecond, (float)eulerXYZ.z); //, ArcManagedFBX.Types.EInterpolationType.eInterpolationLinear);
                    }

                }

                myRotXCurve.KeyModifyEnd();
                myRotYCurve.KeyModifyEnd();
                myRotZCurve.KeyModifyEnd();
            }

            CSGShapeArray childShapeList = group.GetShapes();
            for (int shapeIndex = 0; shapeIndex < childShapeList.GetSize(); shapeIndex++)
            {
                // this little bit of weirdness is due to View3D and Paint3D not being able to have multiple shapes on the same node
                // there may need to be an export option for this at some point
                var subnode = node;
                if (shapeIndex > 0)
                {
                    string subGroupName = groupName + "_" + shapeIndex.ToString();
                    this.helperFunctionsGeneral.MakeNameUnique(ref subGroupName, ref this.uniqueNamesList, ref this.uniqueNamesListCount);
                    subnode = FBXNode.Create(sceneNode, subGroupName);
                    node.AddChild(subnode);
                }

                this.ExportShape(fbxScene, subnode, childShapeList.GetElement(shapeIndex));
                subnode.SetShadingMode(ArcManagedFBX.Types.EShadingMode.eTextureShading);
            }

            CSGGroupArray childGroupList = group.GetChildren();
            for (int groupIndex = 0; groupIndex < childGroupList.GetSize(); groupIndex++)
            {
                this.ExportGroupRecursively(childGroupList.GetElement(groupIndex), fbxScene, node, animationLayer, keyFramesPerSecond);
            }
        }

        private void ExportMaterial(FBXScene fbxScene, FBXNode fbxNode, CSGMaterial material)
        {
            string materialName = "material";
            this.helperFunctionsGeneral.MakeNameUnique(ref materialName, ref this.uniqueNamesList, ref this.uniqueNamesListCount);

            FBXSurfacePhong fbxMaterial = FBXSurfacePhong.Create(fbxScene, materialName);

            var diffuse = material.get_Diffuse();
            fbxMaterial.SetDiffuse(diffuse.r, diffuse.g, diffuse.b);
            fbxMaterial.SetTransparencyFactor(diffuse.a);

            var ambient = material.get_Ambient();
            fbxMaterial.SetAmbient(ambient.r, ambient.g, ambient.b);

            var emissive = material.get_Emissive();
            fbxMaterial.SetEmissive(emissive.r, emissive.g, emissive.b);

            var specular = material.get_Specular();
            fbxMaterial.SetSpecular(specular.r, specular.g, specular.b);
            fbxMaterial.SetSpecularFactor(.1);

            fbxMaterial.SetShadingModel("Phong");
            fbxMaterial.SetShininess(0.0);

            fbxNode.AddMaterial(fbxMaterial);

            if (material.Texture != null)
            {
                var textureFileName = Path.GetFileName(material.Texture.TextureName);
                if (!string.IsNullOrWhiteSpace(textureFileName))
                {
                    var textureId = -1;
                    if (!this.textureIdList.TryGetValue(textureFileName, out textureId))
                    {
                        textureId = this.textureIdList.Count();

                        this.textureIdList.Add(textureFileName, textureId);

                        string textureName = Path.GetFileNameWithoutExtension(textureFileName);
                        this.helperFunctionsGeneral.MakeNameUnique(ref textureName, ref this.uniqueNamesList, ref this.uniqueNamesListCount);

                        FBXFileTexture fbxFileTexture = FBXFileTexture.Create(fbxScene, textureName);
                        fbxFileTexture.SetFileName(textureFileName);
                        fbxFileTexture.SetTextureUse(ArcManagedFBX.Types.ETextureUse.eStandard);
                        fbxFileTexture.SetMappingType(ArcManagedFBX.Types.EMappingType.eUV);
                        fbxFileTexture.SetMaterialUse(ArcManagedFBX.Types.EMaterialUse.eModelMaterial);
                        fbxFileTexture.SetSwapUV(false);
                        fbxFileTexture.SetTranslation(0.0, 0.0);
                        fbxFileTexture.SetScale(1.0, 1.0);
                        fbxFileTexture.SetRotation(0.0, 0.0);

                        this.textureList.Add(fbxFileTexture);

                    }
                    fbxMaterial.DiffuseConnectSrcObjectHelper(this.textureList[textureId]);
                }
            }
        }

        private void ExportShape(FBXScene fbxScene, FBXNode parentNode, CSGShape shape)
        {
            if (shape.RepresentativeEntityType == CSGEntityType.CSGEShape && shape.ShapeType == CSGShapeType.CSGShapeStandard && shape.GetFaceCount() > 0)
            {
                Dictionary<long, int> fbxPointIdList = new Dictionary<long, int>();
                List<CSGVectorLong> fbxPointList = new List<CSGVectorLong>();

                CSGVector[] pointList = null;
                int pointListCount = 0;
                CSGVector[] normalList = null;
                int normalListCount = 0;
                CSGUV[] textureCoordinateList = null;
                int textureCoordinateListCount = 0;
                CSGMaterialFaceList[] materialFaceList = null;
                int materialFaceListCount = 0;
                shape.GetGeometryMaterialSorted(
                    true,
                    false,
                    true,
                    ref pointList,
                    ref pointListCount,
                    ref normalList,
                    ref normalListCount,
                    ref textureCoordinateList,
                    ref textureCoordinateListCount,
                    ref materialFaceList,
                    ref materialFaceListCount);

                foreach (var material in materialFaceList)
                {
                    this.ExportMaterial(fbxScene, parentNode, material.Material);
                    foreach (var face in material.FaceList)
                    {
                        foreach (var facePoint in face.PointList)
                        {
                            long fbxPointKey = facePoint.PointID << 42 | facePoint.NormalID << 21 | facePoint.TextureCoordinateListID[0];
                            CSGVectorLong fbxPoint;
                            if (fbxPointIdList.TryGetValue(fbxPointKey, out int fbxPointId))
                            {
                                fbxPoint = fbxPointList[fbxPointId];
                            }
                            else
                            {
                                fbxPoint = new CSGVectorLong() { X = facePoint.PointID, Y = facePoint.NormalID, Z = facePoint.TextureCoordinateListID[0] };
                                fbxPointId = fbxPointList.Count();

                                fbxPointList.Add(fbxPoint);

                                fbxPointIdList.Add(fbxPointKey, fbxPointId);
                            }
                        }
                    }
                }

                string shapeName = shape.Name;
                if (string.IsNullOrWhiteSpace(shapeName))
                {
                    shapeName = "shape";
                }

                this.helperFunctionsGeneral.MakeNameUnique(ref shapeName, ref this.uniqueNamesList, ref this.uniqueNamesListCount);

                FBXMesh fbxMesh = FBXMesh.Create(fbxScene, shapeName);
                parentNode.AddNodeAttribute(fbxMesh);

                fbxMesh.InitControlPoints(fbxPointIdList.Count);
                fbxMesh.InitMaterialIndices(ArcManagedFBX.Types.EMappingMode.eByPolygon);
                fbxMesh.InitNormals(fbxPointIdList.Count);
                fbxMesh.InitTextureUV(0);
                fbxMesh.InitTextureUVIndices(ArcManagedFBX.Types.EMappingMode.eByControlPoint);

                int id = 0;
                foreach (var point in fbxPointList)
                {
                    FBXVector controlPoint = new FBXVector(pointList[point.X].X, pointList[point.X].Y, -pointList[point.X].Z);
                    fbxMesh.SetControlPointAt(controlPoint, id);

                    FBXVector normal = new FBXVector(normalList[point.Y].X, normalList[point.Y].Y, -normalList[point.Y].Z);
                    fbxMesh.SetControlPointNormalAt(normal, id);

                    ConvertTextureCoordinate(textureCoordinateList, point);

                    fbxMesh.AddTextureUV(new FBXVector2(textureCoordinateList[point.Z].U, textureCoordinateList[point.Z].V));

                    id++;
                }

                int materialId = 0;
                foreach (var material in materialFaceList)
                {
                    foreach (var face in material.FaceList)
                    {
                        fbxMesh.BeginPolygon(materialId, -1, -1, true);
                        foreach (var facePoint in face.PointList.Reverse())
                        {
                            long fbxPointKey = facePoint.PointID << 42 | facePoint.NormalID << 21 | facePoint.TextureCoordinateListID[0];
                            if (fbxPointIdList.TryGetValue(fbxPointKey, out int fbxPointId))
                            {
                                fbxMesh.AddPolygon(fbxPointId, fbxPointId);
                            }
                            else
                            {
                                // should never happen
                                Console.WriteLine("what to do for the impossible?");
                                fbxMesh.AddPolygon(0, 0);
                            }
                        }

                        fbxMesh.EndPolygon();
                    }

                    materialId++;
                }
            }
        }

        private static void ConvertTextureCoordinate(CSGUV[] textureCoordinateList, CSGVectorLong point)
        {
            if (textureCoordinateList[point.Z].V > 1)
            {
                textureCoordinateList[point.Z].V *= -1;
                textureCoordinateList[point.Z].V += 1;
            }
            textureCoordinateList[point.Z].V *= -1;
            textureCoordinateList[point.Z].V += 1;
        }

        private void SetTransform(CSGGroup group, FBXNode node)
        {
            CSGVector position = new CSGVector();
            group.GetPosition(group.Parent, -1, ref position);

            position.Z *= -1;
            node.LclTranslationSet(new FBXVector(position.X, position.Y, position.Z));

            FBXVector eulerXYZRH = GetEulerXYZ(group, -1);

            node.LclRotationSet(new FBXVector(eulerXYZRH.x, eulerXYZRH.y, eulerXYZRH.z));
        }

        private FBXVector GetEulerXYZ(CSGGroup group, float time)
        {
            FBXVector eulerXYZRH;

            CSGMatrix transformLH = new CSGMatrix();
            group.GetTransform(group.Parent, time, ref transformLH);

            var transformRH = ConvertTransformToRightHanded_ThisLittleBitTookFourDaysToFigureOut(transformLH);

            FBXMatrix matrix = new FBXMatrix(
                transformRH.m11,
                transformRH.m12,
                transformRH.m13,
                transformRH.m14,
                transformRH.m21,
                transformRH.m22,
                transformRH.m23,
                transformRH.m24,
                transformRH.m31,
                transformRH.m32,
                transformRH.m33,
                transformRH.m34,
                transformRH.m41,
                transformRH.m42,
                transformRH.m43,
                transformRH.m44);
            FBXQuaternion fbxQuaternionRH = matrix.GetQuaternion();

            eulerXYZRH = fbxQuaternionRH.DecomposeSphericalXYZ();
            return eulerXYZRH;
        }
    }
}