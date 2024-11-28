/*
   Copyright 2021 Esri
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at
       http://www.apache.org/licenses/LICENSE-2.0
   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS, 
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Button = ArcGIS.Desktop.Framework.Contracts.Button;
using MessageBox = System.Windows.Forms.MessageBox;

namespace UtilityNetworkPropertiesExtractor
{
    internal class EditTemplatesButton : Button
    {

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
                MessageBox.Show(ex.Message, "Extracting Fields Info");
            }
            finally
            {
                progDlg.Dispose();
            }
        }

        private static Task EditTemplateMasterAsync()
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
                    sw.WriteLine("Standalone Tables," + Common.GetCountOfAllTablesInMap());
                    sw.WriteLine();


                    //Get all properties defined in the class.  This will be used to generate the CSV file
                    CSVLayout emptyRec = new CSVLayout();
                    PropertyInfo[] properties = Common.GetPropertiesOfClass(emptyRec);

                    //Write column headers based on properties in the class
                    string columnHeader = Common.ExtractClassPropertyNamesToString(properties);
                    sw.WriteLine(columnHeader);

                    List<CSVLayout> csvLayoutList = new List<CSVLayout>();

                    int layerPos = 1;
                    string groupLayerName = string.Empty;
                    string prevGroupLayerName = string.Empty;
                    string layerContainer = string.Empty;
                    string layerType = string.Empty;

                    //Get list of all layers in the map
                    IReadOnlyList<MapMember> mapMemberList = MapView.Active.Map.GetMapMembersAsFlattenedList();
                    foreach (MapMember mapMember in mapMemberList)
                    {
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

                            CIMFeatureLayer layerDef = layer.GetDefinition() as CIMFeatureLayer;
                            if (layerDef != null)
                            {
                                List<CIMEditingTemplate> layerTemplates = layerDef.FeatureTemplates?.ToList();
                                if (layerTemplates.Count > 0)
                                {

                                    //foreach (CIMGroupEditingTemplate groupTemplate in layerTemplates)
                                    //{
                                    //    groupTemplate.Parts
                                    //}

                                    foreach (CIMEditingTemplate template in layerTemplates)
                                    {
                                        CIMRowTemplate cimRowTemplate = template as CIMRowTemplate;
                                        if (cimRowTemplate != null)
                                        {
                                            IDictionary<string, object> templateDict = cimRowTemplate.DefaultValues;
                                            foreach (KeyValuePair<string, object> pair in templateDict)
                                            {

                                                CSVLayout templateRec = new CSVLayout()
                                                {
                                                    LayerPos = layerPos.ToString(),
                                                    LayerType = layerType,
                                                    LayerName = layer.Name,
                                                    GroupLayerName = groupLayerName,
                                                    TemplateName = template.Name,
                                                    FieldName = pair.Key,
                                                    DefaultValue = pair.Value.ToString()
                                                };
                                                csvLayoutList.Add(templateRec);
                                            }
                                        }
                                    }
                                }
                            }
                            //
                        }
                        //Subtype Group Table
                        else if (mapMember is SubtypeGroupTable subtypeGroupTable)
                        {
                            //Include "sub tables" in the report 
                            IReadOnlyList<StandaloneTable> standaloneTablesList = subtypeGroupTable.StandaloneTables;
                            foreach (StandaloneTable standaloneTable in standaloneTablesList)
                                layerPos = InterrogateStandaloneTable(standaloneTable, layerPos, mapMember.Name, ref csvLayoutList);

                        }

                        //Standalone Table
                        else if (mapMember is StandaloneTable standaloneTable)
                        {
                            layerContainer = Common.GetGroupLayerNameForStandaloneTable(standaloneTable);
                            layerPos = InterrogateStandaloneTable(standaloneTable, layerPos, layerContainer, ref csvLayoutList);

                            //Since already added Table info to CsvLayoutList, don't do it again.
                            //addToCsvLayoutList = false;
                        }
                        
                        layerPos += 1;
                    }

                    //Write body of report
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


        private static int InterrogateStandaloneTable(StandaloneTable standaloneTable, int layerPos, string groupLayerName, ref List<CSVLayout> csvLayoutList)
        {

            CSVLayout csvLayout = new CSVLayout()
            {
                GroupLayerName = Common.EncloseStringInDoubleQuotes(groupLayerName),
                LayerName = Common.EncloseStringInDoubleQuotes(standaloneTable.Name),
                LayerPos = layerPos.ToString(),
                LayerType = Common.GetLayerTypeDescription(standaloneTable)
            };

            string tableType = "Table";
            //Subtype Group Table entry
            if (standaloneTable is SubtypeGroupTable)
            {
                csvLayout.GroupLayerName = Common.EncloseStringInDoubleQuotes(standaloneTable.Name);
                csvLayout.LayerName = string.Empty;
                tableType = "Subtype Group Table";
            }

            CIMStandaloneTable cimStandaloneTable = standaloneTable.GetDefinition();
            
            IList<CIMEditingTemplate> cimEditingTemplates = cimStandaloneTable.RowTemplates;
            if (cimEditingTemplates != null)
            {
                foreach (CIMEditingTemplate template in cimEditingTemplates)
                {

                    CIMRowTemplate rowTemplate = template as CIMRowTemplate;
                    if (rowTemplate != null)
                    {
                        IDictionary<string, object> templateDict = rowTemplate.DefaultValues;
                        foreach (KeyValuePair<string, object> pair in templateDict)
                        {
                            csvLayout.TemplateName = template.Name;
                            csvLayout.FieldName = pair.Key;
                            csvLayout.DefaultValue = pair.Value.ToString();
                            
                            csvLayoutList.Add(csvLayout);
                        }
                    }
                }
            }

            layerPos += 1;
            return layerPos; // need to identify next layer position for "table in group layers"
        }


        private class CSVLayout
        {
            public string LayerPos { get; set; }
            public string LayerType { get; set; }
            public string GroupLayerName { get; set; }
            public string LayerName { get; set; }
            public string TemplateName { get; set; }
            public string FieldName { get; set; }
            public string DefaultValue { get; set; }
        }
    }
}