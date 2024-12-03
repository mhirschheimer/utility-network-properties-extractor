﻿using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace UtilityNetworkPropertiesExtractor
{
    internal class EditTemplatesButton : Button
    {

        private const string _subtypeFieldText = "Subtype Field";

        protected async override void OnClick()
        {
            Common.CreateOutputDirectory();
            ProgressDialog progDlg = new ProgressDialog("Extracting Edit Templates Info to: \n" + Common.ExtractFilePath);

            try
            {
                progDlg.Show();
                await EditTemplateMasterAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Extracting Edit Templates Info");
            }
            finally
            {
                progDlg.Dispose();
            }
        }

        public static Task EditTemplateMasterAsync()
        {
            string mesg = string.Empty;

            return QueuedTask.Run(() =>
            {
                string outputFile = Common.BuildCsvNameContainingMapName("EditTemplates");
                using (StreamWriter sw = new StreamWriter(outputFile))
                {
                    //Header information
                    Common.WriteHeaderInfoForMap(sw, "Edit Templates");
                    sw.WriteLine("Layers," + MapView.Active.Map.GetLayersAsFlattenedList().OfType<Layer>().Count());
                    sw.WriteLine("Standalone Tables," + MapView.Active.Map.StandaloneTables.Count);
                    sw.WriteLine();

                    //Get all properties defined in the class.  This will be used to generate the CSV file
                    GroupAndPresetInfo emptyGPI = new GroupAndPresetInfo();
                    PropertyInfo[] gpiProperties = Common.GetPropertiesOfClass(emptyGPI);
                    List<GroupAndPresetInfo> gpiList = new List<GroupAndPresetInfo>();

                    //Get all properties defined in the class.  This will be used to generate the CSV file
                    CSVLayout emptyRec = new CSVLayout();
                    PropertyInfo[] properties = Common.GetPropertiesOfClass(emptyRec);
                    List<CSVLayout> csvLayoutList = new List<CSVLayout>();

                    int layerPos = 1;
                    string groupLayerName = string.Empty;
                    string prevGroupLayerName = string.Empty;
                    string layerContainer = string.Empty;
                    string layerType = string.Empty;

                    //Get datasouces in map.  This will be used to get domain descriptions on defaulted values (if applicable)
                    List<DataSourceInMap> DataSourceInMapList = DataSourcesInMapHelper.GetDataSourcesInMap();

                    //Get list of all layers in the map
                    //IReadOnlyList<MapMember> mapMemberList = MapView.Active.Map.GetMapMembersAsFlattenedList();
                    List<MapMember> mapMemberList = MapView.Active.Map.GetLayersAsFlattenedList().OfType<MapMember>().ToList();
                    foreach (MapMember mapMember in mapMemberList)
                    {
                        //Determine generic layer information
                        if (mapMember is Layer layer)
                        {
                            //Determine if in a group layer
                            layerContainer = layer.Parent.ToString();
                            if (layerContainer != MapView.Active.Map.Name) // Group layer
                            {
                                if (layerContainer != prevGroupLayerName)
                                    prevGroupLayerName = layerContainer;
                            }
                            else
                                layerContainer = string.Empty;

                            layerType = Common.GetLayerTypeDescription(layer);
                            switch (layerType)
                            {
                                case "Annotation Layer":
                                case "Group Layer":
                                case "Subtype Group Layer":
                                case "Utility Network Layer":
                                    groupLayerName = Common.EncloseStringInDoubleQuotes(layer.Name);
                                    break;
                                default:
                                    groupLayerName = Common.EncloseStringInDoubleQuotes(layerContainer);
                                    break;
                            }
                        }

                        //Basic FeatureLayer
                        if (mapMember is BasicFeatureLayer basicFeatureLayer)
                        {
                            InterrogateFeatureLayer(basicFeatureLayer, layerPos, groupLayerName, layerType, DataSourceInMapList, ref gpiList, ref csvLayoutList);
                        }

                        //Group Layer
                        else if (mapMember is GroupLayer groupLayer)
                        {
                            CSVLayout templateRec = new CSVLayout()
                            {
                                LayerPos = layerPos.ToString(),
                                LayerType = layerType,
                                GroupLayerName = groupLayerName
                            };

                            csvLayoutList.Add(templateRec);
                        }

                        //Subtype Group Layer
                        else if (mapMember is SubtypeGroupLayer subtypeGroupLayer)
                        {
                            CSVLayout templateRec = new CSVLayout()
                            {
                                LayerPos = layerPos.ToString(),
                                LayerType = layerType,
                                GroupLayerName = groupLayerName
                            };

                            csvLayoutList.Add(templateRec);
                        }
                                              
                        //Standalone Table
                        else if (mapMember is StandaloneTable standaloneTable)
                        {
                            TableDefinition tableDefinition = getTableDefinitionOfMapMember(DataSourceInMapList, standaloneTable);
                            IReadOnlyList<Field> fieldsList = tableDefinition.GetFields();
                            layerPos = InterrogateStandaloneTable(standaloneTable, layerPos, string.Empty, tableDefinition, fieldsList, ref csvLayoutList);
                        }

                        layerPos += 1;
                    }

                    //Write group and present templates to csv
                    string gpiColumnHeader = Common.ExtractClassPropertyNamesToString(gpiProperties);
                    sw.WriteLine(gpiColumnHeader);

                    foreach (GroupAndPresetInfo gpiRow in gpiList)
                    {
                        string output = Common.ExtractClassValuesToString(gpiRow, gpiProperties);
                        sw.WriteLine(output);
                    }

                    sw.WriteLine("");

                    //Write edit templates to csv
                    string columnHeader = Common.ExtractClassPropertyNamesToString(properties);
                    sw.WriteLine(columnHeader);

                    foreach (CSVLayout row in csvLayoutList)
                    {
                        string output = Common.ExtractClassValuesToString(row, properties);
                        sw.WriteLine(output);
                    }

                    sw.Flush();
                    sw.Close();
                }
            });
        }

        private static void InterrogateFeatureLayer(BasicFeatureLayer basicFeatureLayer, int layerPos, string groupLayerName, string layerType, List<DataSourceInMap> DataSourceInMapList, ref List<GroupAndPresetInfo> gpiList, ref List<CSVLayout> csvLayoutList)
        {
            //Determine if layer has any edit templates
            CIMFeatureLayer layerDef = basicFeatureLayer.GetDefinition() as CIMFeatureLayer;
            if (layerDef != null)
            {
                List<CIMEditingTemplate> cimEditingTemplateList = layerDef.FeatureTemplates?.ToList();
                if (cimEditingTemplateList != null)
                {
                    if (cimEditingTemplateList.Count > 0)
                    {
                        FeatureClassDefinition fcDefinition = getFeatureClassDefinitionOfMapMember(DataSourceInMapList, basicFeatureLayer);
                        if (fcDefinition == null)
                            return;

                        FeatureLayer featureLayer = basicFeatureLayer as FeatureLayer;
                        IReadOnlyList<Field> fieldsList = fcDefinition.GetFields();
                        IReadOnlyList<Subtype> subtypesList = fcDefinition.GetSubtypes();

                        Subtype subtype = null;
                        if (featureLayer.IsSubtypeLayer && subtypesList.Count != 0)
                            subtype = subtypesList.Where(x => x.GetCode() == featureLayer.SubtypeValue).FirstOrDefault();

                        foreach (CIMEditingTemplate editingTemplate in cimEditingTemplateList)
                        {
                            //Group or Preset templates
                            if (editingTemplate is CIMGroupEditingTemplate cimGroupEditingTemplate)
                            {
                                //Add basepart (if exists)
                                CIMGroupEditingTemplatePart basePart = cimGroupEditingTemplate.BasePart;
                                if (basePart != null)
                                {
                                    if (!string.IsNullOrEmpty(basePart.LayerURI))
                                    {
                                        GroupAndPresetInfo gpi = new GroupAndPresetInfo()
                                        {
                                            LayerPos = layerPos.ToString(),
                                            LayerType = layerType,
                                            GroupLayerName = groupLayerName,
                                            LayerName = Common.EncloseStringInDoubleQuotes(basicFeatureLayer.Name),
                                            GroupOrPresetName = Common.EncloseStringInDoubleQuotes(cimGroupEditingTemplate.Name),
                                            FeaturesOrBuilders = basePart.LayerURI
                                        };

                                        gpiList.Add(gpi);
                                    }
                                }

                                //Add the componets to the group/preset template.
                                string featuresURI = string.Empty;
                                foreach (CIMGroupEditingTemplatePart cimGroupEditingTemplatePart in cimGroupEditingTemplate.Parts)
                                {
                                    if (cimGroupEditingTemplatePart.TransformationID == "esri_editing_un_association_builder")
                                        featuresURI = "UN Association Builder";
                                    else
                                        featuresURI = cimGroupEditingTemplatePart.LayerURI;

                                    GroupAndPresetInfo gpi = new GroupAndPresetInfo()
                                    {
                                        LayerPos = layerPos.ToString(),
                                        LayerType = layerType,
                                        GroupLayerName = groupLayerName,
                                        LayerName = Common.EncloseStringInDoubleQuotes(basicFeatureLayer.Name),
                                        GroupOrPresetName = Common.EncloseStringInDoubleQuotes(cimGroupEditingTemplate.Name),
                                        FeaturesOrBuilders = featuresURI
                                    };

                                    gpiList.Add(gpi);
                                }
                            }

                            //Edit templates
                            else if (editingTemplate is CIMFeatureTemplate cimFeatureTemplate)
                            {
                                if (cimFeatureTemplate.Tags != "Hidden")
                                {
                                    string dictValue = string.Empty;
                                    string domainName = string.Empty;
                                    string domainDescription = string.Empty;

                                    if (cimFeatureTemplate.DefaultValues != null)
                                    {
                                        IDictionary<string, object> templateDict = cimFeatureTemplate.DefaultValues;
                                        foreach (KeyValuePair<string, object> pair in templateDict)
                                        {
                                            domainDescription = string.Empty;
                                            domainName = string.Empty;

                                            if (pair.Value == null)
                                                dictValue = string.Empty;
                                            else
                                            {
                                                dictValue = pair.Value.ToString();

                                                //now check if the field has a domain value
                                                Field field = fieldsList.Where(x => x.Name.ToLower() == pair.Key.ToLower()).FirstOrDefault();
                                                if (field != null)
                                                {
                                                    if (field.Name.ToLower() == fcDefinition.GetSubtypeField().ToLower())
                                                    {
                                                        domainName = _subtypeFieldText;

                                                        if (featureLayer.IsSubtypeLayer)
                                                            domainDescription = subtype.GetName();
                                                        else
                                                        {
                                                            Subtype thisSubtype = subtypesList.Where(x => x.GetCode().ToString() == dictValue).FirstOrDefault();
                                                            domainDescription = thisSubtype.GetName();
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Domain domain = field.GetDomain(subtype);
                                                        if (domain != null)
                                                        {
                                                            if (domain is CodedValueDomain codedValueDomain)
                                                            {
                                                                if (pair.Value != null)
                                                                {
                                                                    if (!string.IsNullOrEmpty(pair.Value.ToString()))
                                                                    {
                                                                        domainDescription = Common.GetCodedValueDomainValue(codedValueDomain, dictValue);
                                                                        domainName = domain.GetName();
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            //Write out properties of the edit template
                                            CSVLayout templateRec = new CSVLayout()
                                            {
                                                LayerPos = layerPos.ToString(),
                                                LayerType = layerType,
                                                LayerName = Common.EncloseStringInDoubleQuotes(basicFeatureLayer.Name),
                                                GroupLayerName = groupLayerName,
                                                TemplateName = Common.EncloseStringInDoubleQuotes(editingTemplate.Name),
                                                FieldName = pair.Key,
                                                DefaultValue = dictValue,
                                                DomainDescription = domainDescription,
                                                DomainName = domainName,
                                                CIMPath = basicFeatureLayer.URI
                                            };
                                            csvLayoutList.Add(templateRec);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static int InterrogateStandaloneTable(StandaloneTable standaloneTable, int layerPos, string groupLayerName, TableDefinition tableDefinition, IReadOnlyList<Field> fieldsList, ref List<CSVLayout> csvLayoutList)
        {
            //Get CIM defintion for standalone table
            CIMStandaloneTable cimStandaloneTableDef = standaloneTable.GetDefinition();

            IList<CIMEditingTemplate> cimEditingTemplateList = cimStandaloneTableDef.RowTemplates;
            if (cimEditingTemplateList != null)
            {
                foreach (CIMEditingTemplate cimEditingTemplate in cimEditingTemplateList)
                {
                    CIMFeatureTemplate cimFeatureTemplate = cimEditingTemplate as CIMFeatureTemplate;
                    if (cimFeatureTemplate != null)
                    {
                        string dictValue = string.Empty;
                        string domainName = string.Empty;
                        string domainDescription = string.Empty;

                        if (cimFeatureTemplate.DefaultValues != null)
                        {
                            IDictionary<string, object> templateDict = cimFeatureTemplate.DefaultValues;
                            foreach (KeyValuePair<string, object> pair in templateDict)
                            {
                                domainDescription = string.Empty;
                                domainName = string.Empty;

                                if (pair.Value == null)
                                    dictValue = string.Empty;
                                else
                                {
                                    dictValue = pair.Value.ToString();

                                    //now check if the field has a domain value
                                    Field field = fieldsList.Where(x => x.Name.ToLower() == pair.Key.ToLower()).FirstOrDefault();
                                    if (field != null)
                                    {
                                        Domain domain = field.GetDomain();
                                        if (domain != null)
                                        {
                                            if (domain is CodedValueDomain codedValueDomain)
                                            {
                                                if (pair.Value != null)
                                                {
                                                    if (!string.IsNullOrEmpty(pair.Value.ToString()))
                                                    {
                                                        domainDescription = Common.GetCodedValueDomainValue(codedValueDomain, dictValue);
                                                        domainName = domain.GetName();
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                //Write out properties of the edit template
                                CSVLayout templateRec = new CSVLayout()
                                {
                                    LayerPos = layerPos.ToString(),
                                    LayerType = "Standalone Table",
                                    LayerName = Common.EncloseStringInDoubleQuotes(standaloneTable.Name),
                                    GroupLayerName = groupLayerName,
                                    TemplateName = Common.EncloseStringInDoubleQuotes(cimEditingTemplate.Name),
                                    FieldName = pair.Key,
                                    DefaultValue = dictValue,
                                    DomainDescription = domainDescription,
                                    DomainName = domainName,
                                    CIMPath = standaloneTable.URI
                                };
                                csvLayoutList.Add(templateRec);
                            }
                        }
                    }
                }
            }

            layerPos += 1;
            return layerPos; // need to identify next layer position for "table in group layers"
        }

        private static FeatureClassDefinition getFeatureClassDefinitionOfMapMember(List<DataSourceInMap> DataSourceInMapList, MapMember mapMember)
        {
            foreach (DataSourceInMap dataSource in DataSourceInMapList)
            {
                FeatureClassDefinition fcDefinition;
                IReadOnlyList<FeatureClassDefinition> fcDefinitions = dataSource.Geodatabase.GetDefinitions<FeatureClassDefinition>();

                if (mapMember is FeatureLayer featureLayer)
                {
                    fcDefinition = fcDefinitions.Where(x => x.GetName() == featureLayer.GetFeatureClass().GetName()).FirstOrDefault();
                    if (fcDefinition != null)
                        return fcDefinition;
                }
            }
            return null;
        }

        private static TableDefinition getTableDefinitionOfMapMember(List<DataSourceInMap> DataSourceInMapList, MapMember mapMember)
        {
            foreach (DataSourceInMap dataSource in DataSourceInMapList)
            {
                TableDefinition tableDefinition;
                IReadOnlyList<TableDefinition> tableDefinitions = dataSource.Geodatabase.GetDefinitions<TableDefinition>();

                if (mapMember is StandaloneTable standaloneTable)
                {
                    tableDefinition = tableDefinitions.Where(x => x.GetName() == standaloneTable.GetTable().GetName()).FirstOrDefault();
                    if (tableDefinition != null)
                    {
                        return tableDefinition;
                    }
                }
            }
            return null;
        }

        private class GroupAndPresetInfo
        {
            public string GroupAndPresetTemplates { get; set; }
            public string LayerPos { get; set; }
            public string LayerType { get; set; }
            public string GroupLayerName { get; set; }
            public string LayerName { get; set; }
            public string GroupOrPresetName { get; set; }
            public string FeaturesOrBuilders { get; set; }
        }

        private class CSVLayout
        {
            public string EditTemplates { get; set; }
            public string LayerPos { get; set; }
            public string LayerType { get; set; }
            public string GroupLayerName { get; set; }
            public string LayerName { get; set; }
            public string TemplateName { get; set; }
            public string FieldName { get; set; }
            public string DefaultValue { get; set; }
            public string DomainDescription { get; set; }
            public string DomainName { get; set; }
            public string CIMPath { get; set; }
        }
    }
}
