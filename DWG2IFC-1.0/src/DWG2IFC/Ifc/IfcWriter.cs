using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Plant3D.IfcExporter.Geometry;
using Plant3D.IfcExporter.Plant;
using Plant3D.IfcExporter.Settings;

namespace Plant3D.IfcExporter.Ifc
{
    public class ExportItem
    {
        public ExtractedMesh Mesh { get; set; } = new();
        public PlantComponentData Data { get; set; } = new();
        public ClassificationResult Classification { get; set; } = new();
    }

    public class IfcWriter
    {
        private readonly ExportConfig _config;
        private int _idCounter = 0;
        private readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        public IfcWriter(ExportConfig config)
        {
            _config = config;
        }

        private int NextId() => ++_idCounter;

        public void Export(string filePath, IReadOnlyList<ExportItem> items, Action<int, int>? progressCallback = null)
        {
            _idCounter = 0;
            var isIfc4 = _config.Schema == IfcSchemaVersion.IFC4;

            using var sw = new StreamWriter(filePath, false, new UTF8Encoding(false), 65536);

            // 1. ISO-10303-21 Header
            sw.WriteLine("ISO-10303-21;");
            sw.WriteLine("HEADER;");
            sw.WriteLine(isIfc4
                ? "FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');"
                : "FILE_DESCRIPTION(('ViewDefinition [CoordinationView_V2.0]'),'2;1');");

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", _ci);
            sw.WriteLine($"FILE_NAME('{Escape(Path.GetFileName(filePath))}','{timestamp}',('Plant3D Engineer'),('Plant 3D Organization'),'Plant3D IFC Exporter','AutoCAD Plant 3D 2025','');");
            sw.WriteLine(isIfc4 ? "FILE_SCHEMA(('IFC4'));" : "FILE_SCHEMA(('IFC2X3'));");
            sw.WriteLine("ENDSEC;");
            sw.WriteLine("DATA;");

            // 2. Project Context & Units
            int idOrigin = NextId();
            sw.WriteLine($"#{idOrigin}=IFCCARTESIANPOINT((0.,0.,0.));");

            int idDirZ = NextId();
            sw.WriteLine($"#{idDirZ}=IFCDIRECTION((0.,0.,1.));");

            int idDirX = NextId();
            sw.WriteLine($"#{idDirX}=IFCDIRECTION((1.,0.,0.));");

            int idPlacementWorld = NextId();
            sw.WriteLine($"#{idPlacementWorld}=IFCAXIS2PLACEMENT3D(#{idOrigin},#{idDirZ},#{idDirX});");

            int idModelContext = NextId();
            sw.WriteLine($"#{idModelContext}=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#{idPlacementWorld},$);");

            int idBodySubContext = NextId();
            sw.WriteLine(isIfc4
                ? $"#{idBodySubContext}=IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#{idModelContext},$,.MODEL_VIEW.,$);"
                : $"#{idBodySubContext}=IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#{idModelContext},$,.MODEL_VIEW.,$);");

            // Units
            int idUnitLen = NextId();
            if (_config.UseMillimetres)
                sw.WriteLine($"#{idUnitLen}=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);");
            else
                sw.WriteLine($"#{idUnitLen}=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);");

            int idUnitArea = NextId();
            sw.WriteLine($"#{idUnitArea}=IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.);");

            int idUnitVol = NextId();
            sw.WriteLine($"#{idUnitVol}=IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.);");

            int idUnitAng = NextId();
            sw.WriteLine($"#{idUnitAng}=IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.);");

            int idUnits = NextId();
            sw.WriteLine($"#{idUnits}=IFCUNITASSIGNMENT((#{idUnitLen},#{idUnitArea},#{idUnitVol},#{idUnitAng}));");

            // Owner History (IFC2x3 requires it, IFC4 optional)
            int idPerson = NextId();
            sw.WriteLine($"#{idPerson}=IFCPERSON($,'Plant3D',$,$,$,$,$,$);");
            int idOrg = NextId();
            sw.WriteLine($"#{idOrg}=IFCORGANIZATION($,'AutoCAD Plant 3D',$,$,$);");
            int idPersonOrg = NextId();
            sw.WriteLine($"#{idPersonOrg}=IFCPERSONANDORGANIZATION(#{idPerson},#{idOrg},$);");
            int idApp = NextId();
            sw.WriteLine($"#{idApp}=IFCAPPLICATION(#{idOrg},'2025','Plant3D IFC Exporter','P3D_IFC');");

            int idOwnerHistory = NextId();
            long epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            sw.WriteLine($"#{idOwnerHistory}=IFCOWNERHISTORY(#{idPersonOrg},#{idApp},.READWRITE.,.NOCHANGE.,{epoch},#{idPersonOrg},#{idApp},{epoch});");

            // 3. Spatial Hierarchy: Project -> Site -> Building -> Storey
            string guidProj = IfcGuid.CreateDeterministicGuid("Project_" + _config.ProjectName);
            int idProject = NextId();
            sw.WriteLine($"#{idProject}=IFCPROJECT('{guidProj}',#{idOwnerHistory},'{Escape(_config.ProjectName)}',$,$,$,$,(#{idModelContext}),#{idUnits});");

            string guidSite = IfcGuid.CreateDeterministicGuid("Site_" + _config.SiteName);
            int idSite = NextId();
            int idSitePlacement = NextId();
            sw.WriteLine($"#{idSitePlacement}=IFCLOCALPLACEMENT($,#{idPlacementWorld});");
            sw.WriteLine($"#{idSite}=IFCSITE('{guidSite}',#{idOwnerHistory},'{Escape(_config.SiteName)}',$,$,#{idSitePlacement},$,$,.ELEMENT.,$,$,$,$,$);");

            string guidBldg = IfcGuid.CreateDeterministicGuid("Building_" + _config.BuildingName);
            int idBldg = NextId();
            int idBldgPlacement = NextId();
            sw.WriteLine($"#{idBldgPlacement}=IFCLOCALPLACEMENT(#{idSitePlacement},#{idPlacementWorld});");
            sw.WriteLine($"#{idBldg}=IFCBUILDING('{guidBldg}',#{idOwnerHistory},'{Escape(_config.BuildingName)}',$,$,#{idBldgPlacement},$,$,.ELEMENT.,$,$,$);");

            string guidStorey = IfcGuid.CreateDeterministicGuid("Storey_" + _config.StoreyName);
            int idStorey = NextId();
            int idStoreyPlacement = NextId();
            sw.WriteLine($"#{idStoreyPlacement}=IFCLOCALPLACEMENT(#{idBldgPlacement},#{idPlacementWorld});");
            sw.WriteLine($"#{idStorey}=IFCBUILDINGSTOREY('{guidStorey}',#{idOwnerHistory},'{Escape(_config.StoreyName)}',$,$,#{idStoreyPlacement},$,$,.ELEMENT.,0.);");

            // RelAggregates (Project -> Site)
            int idRelAgg1 = NextId();
            sw.WriteLine($"#{idRelAgg1}=IFCRELAGGREGATES('{IfcGuid.NewGuid()}',#{idOwnerHistory},$,$,#{idProject},(#{idSite}));");

            // RelAggregates (Site -> Building)
            int idRelAgg2 = NextId();
            sw.WriteLine($"#{idRelAgg2}=IFCRELAGGREGATES('{IfcGuid.NewGuid()}',#{idOwnerHistory},$,$,#{idSite},(#{idBldg}));");

            // RelAggregates (Building -> Storey)
            int idRelAgg3 = NextId();
            sw.WriteLine($"#{idRelAgg3}=IFCRELAGGREGATES('{IfcGuid.NewGuid()}',#{idOwnerHistory},$,$,#{idBldg},(#{idStorey}));");

            var exportedElementIds = new List<int>();
            var styleCache = new Dictionary<(byte R, byte G, byte B), int>();

            double unitScale = _config.UseMillimetres ? 1.0 : 0.001;
            double offX = _config.OffsetToOrigin ? _config.CustomOffsetX : 0.0;
            double offY = _config.OffsetToOrigin ? _config.CustomOffsetY : 0.0;
            double offZ = _config.OffsetToOrigin ? _config.CustomOffsetZ : 0.0;

            // 4. Export Elements
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!item.Mesh.IsValid) continue;

                var mesh = item.Mesh;
                var data = item.Data;

                // Color / Style
                int idStyle;
                var colorKey = (mesh.Red, mesh.Green, mesh.Blue);
                if (!styleCache.TryGetValue(colorKey, out idStyle))
                {
                    int idColor = NextId();
                    double r = mesh.Red / 255.0;
                    double g = mesh.Green / 255.0;
                    double b = mesh.Blue / 255.0;
                    sw.WriteLine($"#{idColor}=IFCCOLOURRGB($,{r.ToString("F3", _ci)},{g.ToString("F3", _ci)},{b.ToString("F3", _ci)});");

                    int idShading = NextId();
                    sw.WriteLine($"#{idShading}=IFCSURFACESTYLESHADING(#{idColor});");

                    int idSurfStyle = NextId();
                    sw.WriteLine($"#{idSurfStyle}=IFCSURFACESTYLE('Style_{mesh.Red}_{mesh.Green}_{mesh.Blue}',.BOTH.,(#{idShading}));");

                    idStyle = NextId();
                    sw.WriteLine($"#{idStyle}=IFCPRESENTATIONSTYLEASSIGNMENT((#{idSurfStyle}));");
                    styleCache[colorKey] = idStyle;
                }

                // Geometry creation
                int idGeomSolid;
                if (isIfc4)
                {
                    // IFC4: IfcTriangulatedFaceSet with IfcCartesianPointList3D
                    int idPtList = NextId();
                    var sbPts = new StringBuilder(mesh.Vertices.Count * 32);
                    sbPts.Append($"#{idPtList}=IFCCARTESIANPOINTLIST3D((");
                    for (int vIdx = 0; vIdx < mesh.Vertices.Count; vIdx++)
                    {
                        if (vIdx > 0) sbPts.Append(',');
                        var v = mesh.Vertices[vIdx];
                        double vx = (v.X - offX) * unitScale;
                        double vy = (v.Y - offY) * unitScale;
                        double vz = (v.Z - offZ) * unitScale;
                        sbPts.Append($"({vx.ToString("F4", _ci)},{vy.ToString("F4", _ci)},{vz.ToString("F4", _ci)})");
                    }
                    sbPts.Append("));");
                    sw.WriteLine(sbPts.ToString());

                    int idFaceSet = NextId();
                    var sbFaces = new StringBuilder(mesh.Faces.Count * 24);
                    sbFaces.Append($"#{idFaceSet}=IFCTRIANGULATEDFACESET(#{idPtList},$,.T.,(");
                    for (int fIdx = 0; fIdx < mesh.Faces.Count; fIdx++)
                    {
                        if (fIdx > 0) sbFaces.Append(',');
                        var f = mesh.Faces[fIdx];
                        // 1-based indexing in IFC
                        sbFaces.Append($"({f.A + 1},{f.B + 1},{f.C + 1})");
                    }
                    sbFaces.Append("),$);");
                    sw.WriteLine(sbFaces.ToString());

                    idGeomSolid = idFaceSet;
                }
                else
                {
                    // IFC2x3: IfcFacetedBrep with IfcPolyLoop
                    var faceIds = new List<int>(mesh.Faces.Count);
                    foreach (var f in mesh.Faces)
                    {
                        var va = mesh.Vertices[f.A];
                        var vb = mesh.Vertices[f.B];
                        var vc = mesh.Vertices[f.C];

                        int idPa = NextId();
                        sw.WriteLine($"#{idPa}=IFCCARTESIANPOINT(({((va.X - offX) * unitScale).ToString("F4", _ci)},{((va.Y - offY) * unitScale).ToString("F4", _ci)},{((va.Z - offZ) * unitScale).ToString("F4", _ci)}));");
                        int idPb = NextId();
                        sw.WriteLine($"#{idPb}=IFCCARTESIANPOINT(({((vb.X - offX) * unitScale).ToString("F4", _ci)},{((vb.Y - offY) * unitScale).ToString("F4", _ci)},{((vb.Z - offZ) * unitScale).ToString("F4", _ci)}));");
                        int idPc = NextId();
                        sw.WriteLine($"#{idPc}=IFCCARTESIANPOINT(({((vc.X - offX) * unitScale).ToString("F4", _ci)},{((vc.Y - offY) * unitScale).ToString("F4", _ci)},{((vc.Z - offZ) * unitScale).ToString("F4", _ci)}));");

                        int idLoop = NextId();
                        sw.WriteLine($"#{idLoop}=IFCPOLYLOOP((#{idPa},#{idPb},#{idPc}));");
                        int idBound = NextId();
                        sw.WriteLine($"#{idBound}=IFCFACEOUTERBOUND(#{idLoop},.T.);");
                        int idFace = NextId();
                        sw.WriteLine($"#{idFace}=IFCFACE((#{idBound}));");
                        faceIds.Add(idFace);
                    }

                    int idShell = NextId();
                    sw.WriteLine($"#{idShell}=IFCCLOSEDSHELL(({string.Join(",", faceIds.ConvertAll(fid => "#" + fid))}));");
                    int idBrep = NextId();
                    sw.WriteLine($"#{idBrep}=IFCFACETEDBREP(#{idShell});");
                    idGeomSolid = idBrep;
                }

                // Styled item
                int idStyled = NextId();
                sw.WriteLine($"#{idStyled}=IFCSTYLEDITEM(#{idGeomSolid},(#{idStyle}),$);");

                // Shape Representation
                int idShapeRep = NextId();
                string repType = isIfc4 ? "'Tessellation'" : "'Brep'";
                sw.WriteLine($"#{idShapeRep}=IFCSHAPEREPRESENTATION(#{idBodySubContext},'Body',{repType},(#{idGeomSolid}));");

                // Product Definition Shape
                int idProdShape = NextId();
                sw.WriteLine($"#{idProdShape}=IFCPRODUCTDEFINITIONSHAPE($,$,(#{idShapeRep}));");

                // Local Placement
                int idElemPlacement = NextId();
                sw.WriteLine($"#{idElemPlacement}=IFCLOCALPLACEMENT(#{idStoreyPlacement},#{idPlacementWorld});");

                // Entity creation
                string entityGuid = IfcGuid.CreateDeterministicGuid($"DWG_{data.Handle}_{data.LineNumberTag}_{i}");
                string entityName = !string.IsNullOrWhiteSpace(data.Tag) ? data.Tag :
                                    !string.IsNullOrWhiteSpace(data.Description) ? data.Description :
                                    data.ClassName;
                string entityDesc = !string.IsNullOrWhiteSpace(data.Description) ? data.Description : data.ClassName;

                int idElement = NextId();
                string ifcClass = item.Classification.EntityType.ToString().ToUpperInvariant();
                string predefType = item.Classification.PredefinedType;

                // Write entity according to schema
                if (item.Classification.EntityType == IfcEntityType.IfcPipeSegment)
                {
                    sw.WriteLine($"#{idElement}=IFCPIPESEGMENT('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,{predefType});");
                }
                else if (item.Classification.EntityType == IfcEntityType.IfcPipeFitting)
                {
                    sw.WriteLine($"#{idElement}=IFCPIPEFITTING('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,{predefType});");
                }
                else if (item.Classification.EntityType == IfcEntityType.IfcValve)
                {
                    sw.WriteLine($"#{idElement}=IFCVALVE('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,{predefType});");
                }
                else if (item.Classification.EntityType == IfcEntityType.IfcPump)
                {
                    sw.WriteLine($"#{idElement}=IFCPUMP('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,{predefType});");
                }
                else if (item.Classification.EntityType == IfcEntityType.IfcFlowMovingDevice)
                {
                    sw.WriteLine($"#{idElement}=IFCFLOWMOVINGDEVICE('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,$);");
                }
                else if (item.Classification.EntityType == IfcEntityType.IfcEnergyConversionDevice)
                {
                    sw.WriteLine($"#{idElement}=IFCENERGYCONVERSIONDEVICE('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,$);");
                }
                else if (item.Classification.EntityType == IfcEntityType.IfcBeam)
                {
                    sw.WriteLine($"#{idElement}=IFCBEAM('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,$);");
                }
                else if (item.Classification.EntityType == IfcEntityType.IfcColumn)
                {
                    sw.WriteLine($"#{idElement}=IFCCOLUMN('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,$);");
                }
                else
                {
                    sw.WriteLine($"#{idElement}=IFCBUILDINGELEMENTPROXY('{entityGuid}',#{idOwnerHistory},'{Escape(entityName)}','{Escape(entityDesc)}',$,#{idElemPlacement},#{idProdShape},$,$);");
                }

                exportedElementIds.Add(idElement);

                // Property Sets
                var propertyIds = new List<int>();
                if (_config.ExportPlantProperties || _config.ExportCadProperties)
                {
                    foreach (var kvp in data.CustomProperties)
                    {
                        if (_config.SkipEmptyProperties && string.IsNullOrWhiteSpace(kvp.Value))
                            continue;

                        int idPropVal = NextId();
                        sw.WriteLine($"#{idPropVal}=IFCPROPERTYSINGLEVALUE('{Escape(kvp.Key)}',$,IFCLABEL('{Escape(kvp.Value)}'),$);");
                        propertyIds.Add(idPropVal);
                    }
                }

                if (propertyIds.Count > 0)
                {
                    int idPset = NextId();
                    string psetName = string.IsNullOrWhiteSpace(_config.PropertySetPrefix) ? "Pset_Plant3D_Piping" : _config.PropertySetPrefix;
                    sw.WriteLine($"#{idPset}=IFCPROPERTYSET('{IfcGuid.NewGuid()}',#{idOwnerHistory},'{Escape(psetName)}',$,({string.Join(",", propertyIds.ConvertAll(pid => "#" + pid))}));");

                    int idRelProps = NextId();
                    sw.WriteLine($"#{idRelProps}=IFCRELDEFINESBYPROPERTIES('{IfcGuid.NewGuid()}',#{idOwnerHistory},$,$,(#{idElement}),#{idPset});");
                }

                progressCallback?.Invoke(i + 1, items.Count);
            }

            // Contain elements in Storey
            if (exportedElementIds.Count > 0)
            {
                int idRelContained = NextId();
                var elemRefs = string.Join(",", exportedElementIds.ConvertAll(eid => "#" + eid));
                sw.WriteLine($"#{idRelContained}=IFCRELCONTAINEDINSPATIALSTRUCTURE('{IfcGuid.NewGuid()}',#{idOwnerHistory},'SpatialContainment',$,({elemRefs}),#{idStorey});");
            }

            sw.WriteLine("ENDSEC;");
            sw.WriteLine("END-ISO-10303-21;");
            sw.Flush();
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\\", "\\\\").Replace("'", "''");
        }
    }
}
