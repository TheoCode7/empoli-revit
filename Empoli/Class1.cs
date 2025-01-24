using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Analytics.Interfaces;
using Microsoft.Analytics.Interfaces.Streaming;
using Microsoft.Analytics.Types.Sql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static Empoli.Executing;
using Autodesk.Revit.DB.Structure;
using System.Linq.Expressions;
using System.Collections.ObjectModel;
using System.Net;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Xml.Linq;


namespace Empoli
{

    public class App : IExternalApplication
    {

        public Result OnStartup(UIControlledApplication application)
        {


            RibbonPanel ribbonPanel = application.CreateRibbonPanel("Empoli");

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData("cmdMyTest", "Empoli", thisAssemblyPath, "Empoli.Executing");

            PushButton pushButton = ribbonPanel.AddItem(buttonData) as PushButton;

            pushButton.ToolTip = "This is Empoli Plug-in";

            Uri urlImage = new Uri("/Empoli;component/Resources/empoli.png", UriKind.Relative);

            BitmapImage bitmap = new BitmapImage(urlImage);
            pushButton.LargeImage = bitmap;

            //string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "empoli.png");

            //if (File.Exists(filePath))
            //{
            //    BitmapImage bitmap = new BitmapImage();
            //    bitmap.BeginInit();
            //    bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            //    bitmap.CacheOption = BitmapCacheOption.OnLoad;
            //    bitmap.EndInit();

            //    Console.WriteLine("BitmapImage criado com sucesso!");
            //    pushButton.LargeImage = bitmap;
            //}
            //else
            //{
            //    Console.WriteLine("Arquivo não encontrado no caminho: " + filePath);
            //}


            return Result.Succeeded;

        }


        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
        

    }

    [Transaction(TransactionMode.Manual)]

    public class Executing : IExternalCommand
    {
        public class Point
        {
            public float x { get; set; }
            public float y { get; set; }
        }

        public class ImageResponse
        {
            public string height { get; set; }
            public string width { get; set; }
        }


        public class ResponseLoginAPI
        {
            public string Id { get; set; }
        }
        public class ResponseAPI
        {
            public List<Prediction> walls { get; set; }
        }

        public class ImagePrediction
        {
            public float width { get; set; }

            public float height { get; set; }
        }


        public class ResponseFurnitureAPI
        {
            public List<PredictionFurniture> predictions { get; set; }

            public string inference_id { get; set; }

            public float time { get; set; }

            public ImagePrediction image { get; set; }
        }

        public class Map
        {
            public float x0 { get; set; }
            public float y0 { get; set; }
            public float x1 { get; set; }
            public float y1 { get; set; }
        }

        public class Prediction
        {
            public float x { get; set; }
            public float y { get; set; }
            public float width { get; set; }
            public float height { get; set; }
            public float confidence { get; set; }
            public string @class { get; set; } // @ é usado porque "class" é uma palavra reservada no C#
            public int class_id { get; set; }
            public string detection_id { get; set; }
            public bool has_window { get; set; }
            public bool has_door { get; set; }
            public List<Point> points { get; set; }  // Lista de pontos

            public List<Prediction> children { get; set;  }
        }

        public class PredictionFurniture
        {
            public float x { get; set; }
            public float y { get; set; }
            public float width { get; set; }
            public float height { get; set; }
            public float confidence { get; set; }
            public string @class { get; set; }
            public int class_id { get; set; }
            public string detection_id { get; set; }

        }



        static AddInId appId = new AddInId(new Guid("60704B25-87E8-45CA-ABC8-0D1FC461E3EA"));


        private FamilySymbol GetDoorType(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_Doors);

            List<string> doorSymbols = new List<string>();

            foreach (FamilySymbol symbol in collector)
            {
                string symbolName = symbol.Name;
                doorSymbols.Add(symbolName);
            }

            string result = string.Join(", ", doorSymbols);

            // TaskDialog.Show("Family Symbols", result);



            foreach (FamilySymbol symbol in collector)
            {
                if (symbol.Name.StartsWith("0915", StringComparison.InvariantCultureIgnoreCase))
                {
                    return symbol;
                }
            }

            return null;
        }


        private FamilySymbol GetWindowType(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_Windows);

            List<string> windowSymbols = new List<string>();

            foreach (FamilySymbol symbol in collector)
            {
                string symbolName = symbol.Name;
                windowSymbols.Add(symbolName);
            }

            string result = string.Join(", ", windowSymbols);

            // TaskDialog.Show("Family Symbols", result);



            foreach (FamilySymbol symbol in collector)
            {
                if (symbol.Name.StartsWith("0915", StringComparison.InvariantCultureIgnoreCase))
                {
                    return symbol;
                }
            }

            return null;
        }


        public void CreateDoor(Wall wall, ExternalCommandData commandData, Prediction item, Map map, Level li, ref int countErrors)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }



            using (Transaction t = new Transaction(doc, "Create Door"))
            {
                try
                {
                    t.Start();

                    FamilySymbol doorType = GetDoorType(doc);
                    if (doorType != null && !doorType.IsActive)
                    {
                        doorType.Activate();
                    }

                    Family f = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                       .FirstOrDefault(q => q.FamilyCategoryId.IntegerValue == (int)BuiltInCategory.OST_Doors
                                     ) as Family;

                    List<string> doorSymbols = new List<string>();

                    doorSymbols.Add(f.Name);

                    string result = string.Join(", ", doorSymbols);

                    if (f == null)
                    {
                        TaskDialog.Show("Error", "no family");
                        return;
                    }

                    FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family));


                    List<Family> door = collector.Cast<Family>()
                        .Where((Family fe) => fe.Name.StartsWith("M_Door"))
                        .ToList();

                    FamilySymbol fs = f.GetFamilySymbolIds().Select(q => doc.GetElement(q)).Cast<FamilySymbol>().First(q => q.Name.StartsWith("0915", StringComparison.InvariantCultureIgnoreCase));

                    // Reference r = uidoc.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element,"Pick point on wall");
                    // XYZ point = r.GlobalPoint;
                    // Element host = doc.GetElement(r);
                    // string elementAsString = $"Id: {host.Id}, Name: {host.Name}, Category: {host.Category.Name}";



                    Family doorFamily = door.First();

                    if (doorFamily.GetFamilySymbolIds().Count > 0)
                    {
                        FamilySymbol doorSymbol = doc.GetElement(doorFamily.GetFamilySymbolIds().First()) as FamilySymbol;

                        foreach (Prediction itemDoor in item.children)
                    {

                        if (itemDoor.@class == "Door")
                        {

                            if (map.y0 == map.y1)
                            {
                                if (itemDoor.x > map.x0 && itemDoor.x < map.x1)
                                {

                                    if (!doorSymbol.IsActive)
                                    {
                                        doorSymbol.Activate();
                                        doc.Regenerate();
                                    }

                                    //Parameter larguraParam = doorSymbol.get_Parameter(BuiltInParameter.DOOR_WIDTH); 
                                    //Parameter alturaParam = doorSymbol.get_Parameter(BuiltInParameter.DOOR_HEIGHT); 

                                    //if (larguraParam != null)
                                    //{
                                    //    double novaLarguraEmMetros = 4.5; 
                                    //    double novaLarguraEmPes = novaLarguraEmMetros * 3.28084; 
                                    //    larguraParam.Set(novaLarguraEmPes);
                                    //}

                                    //if (alturaParam != null)
                                    //{
                                    //    double novaAlturaEmMetros = 10;
                                    //    double novaAlturaEmPes = novaAlturaEmMetros * 3.28084; 
                                    //    alturaParam.Set(novaAlturaEmPes);
                                    //}



                                    XYZ doorLocation = new XYZ(itemDoor.x, map.y0, -14);

                                    FamilyInstance customWindow = doc.Create.NewFamilyInstance(
                                      doorLocation,
                                      doorSymbol,
                                      wall,
                                      li,
                                      Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                }
                                else
                                {

                                    if (!doorSymbol.IsActive)
                                    {
                                        doorSymbol.Activate();
                                        doc.Regenerate();
                                    }

                                    //Parameter larguraParam = doorSymbol.get_Parameter(BuiltInParameter.DOOR_WIDTH);
                                    //Parameter alturaParam = doorSymbol.get_Parameter(BuiltInParameter.DOOR_HEIGHT);

                                    //if (larguraParam != null)
                                    //{
                                    //    double novaLarguraEmMetros = 4.5;
                                    //    double novaLarguraEmPes = novaLarguraEmMetros * 3.28084;
                                    //    larguraParam.Set(novaLarguraEmPes);
                                    //}

                                    //if (alturaParam != null)
                                    //{
                                    //    double novaAlturaEmMetros = 10;
                                    //    double novaAlturaEmPes = novaAlturaEmMetros * 3.28084;
                                    //    alturaParam.Set(novaAlturaEmPes);
                                    //}


                                    XYZ doorLocation = new XYZ(map.x1 - map.x0, map.y0, -14);

                                    FamilyInstance customWindow = doc.Create.NewFamilyInstance(
                                      doorLocation,
                                      doorSymbol,
                                      wall,
                                      li,
                                      Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                }



                            }

                        }


                        //if (map.x0 == map.x1)
                        //{
                        //    if (itemDoor.y > map.y0 && itemDoor.y < map.y1)
                        //    {
                        //        XYZ doorLocation = new XYZ(map.x0, itemDoor.y, 15);

                        //        FamilyInstance customWindow = doc.Create.NewFamilyInstance(
                        //          doorLocation,
                        //          fs,
                        //          wall,
                        //          li,
                        //          Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        //    } else
                        //    {
                        //        XYZ doorLocation = new XYZ(map.x0, map.y1 - map.y0, 15);

                        //        FamilyInstance customWindow = doc.Create.NewFamilyInstance(
                        //          doorLocation,
                        //          fs,
                        //          wall,
                        //          li,
                        //          Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        //    }
                        //}


                    }


                    }
                    t.Commit();
                }
                catch (Exception ex)
                {

                    if (countErrors == 0)
                    {
                        TaskDialog.Show("Alert", "Our code uses user resources to create the Door element. To do this, your door must contain the term \"Door\" in the descriptive name.");
                    }
                    countErrors++;
                    t.RollBack();
                }
            }
        }



        public void CreateWindow(Wall wall, ExternalCommandData commandData, Prediction item, Map map, Level li, ref int countErrorsWindow)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            using (Transaction t = new Transaction(doc, "Create Window"))
            {
                try
                {
                    t.Start();

                    FamilySymbol windowType = GetWindowType(doc);
                    if (windowType != null && !windowType.IsActive)
                    {
                        windowType.Activate();
                    }

                    Family f = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                       .FirstOrDefault(q => q.FamilyCategoryId.IntegerValue == (int)BuiltInCategory.OST_Windows
                                     ) as Family;

                    FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family));


                    List<Family> window = collector.Cast<Family>()
                        .Where((Family fe) => fe.Name.Contains("Window"))
                        .ToList();

                    // Exibe o ID e o nome de cada janela encontrada
                    //string windowInfo = "Janelas encontradas:\n";
                    //foreach (Family window in windows)
                    //{
                    //    windowInfo += $"ID: {window.Id.IntegerValue}, Nome: {window.Name}\n";
                    //}

                    //TaskDialog.Show("Instâncias de Janelas", window[0].Name);




                    ICollection<ElementId> symbolIds = f.GetFamilySymbolIds();

                    // Converte cada ID em uma string e junta-os com uma vírgula, por exemplo
                    string symbolIdsString = string.Join(", ", symbolIds.Select(id => id.IntegerValue.ToString()));

                    // Exibe o resultado
                    //TaskDialog.Show("Symbol IDs", symbolIdsString);


                    List<string> windowSymbols = new List<string>();

                    windowSymbols.Add(f.Name);

                    string result = string.Join(", ", windowSymbols);

                    if (f == null)
                    {
                        TaskDialog.Show("Error", "no family");
                        return;
                    }


                    Family windowFamily = window.First();

                    if (windowFamily.GetFamilySymbolIds().Count > 0)
                    {
                        FamilySymbol windowSymbol = doc.GetElement(windowFamily.GetFamilySymbolIds().First()) as FamilySymbol;


                        foreach (Prediction win in item.children)
                        {
                            if (win.@class == "Window")
                            {

                                if (map.y0 == map.y1)
                                {
                                    if (win.x > map.x0 && win.x < map.x1)
                                    {
                                        //TaskDialog.Show("====", "itemDoor.x==>" + itemDoor.x.ToString());
                                        //TaskDialog.Show(">>>>", "map.y0==>" + map.y0.ToString());
                                        XYZ doorLocation = new XYZ(win.x, map.y0, -8);



                                        if (!windowSymbol.IsActive)
                                        {
                                            windowSymbol.Activate();
                                            doc.Regenerate();
                                        }

                                        //Parameter larguraParam = windowSymbol.get_Parameter(BuiltInParameter.WINDOW_WIDTH);
                                        //Parameter alturaParam = windowSymbol.get_Parameter(BuiltInParameter.WINDOW_HEIGHT);

                                        //if (larguraParam != null)
                                        //{
                                        //    double novaLarguraEmMetros = 9;
                                        //    double novaLarguraEmPes = novaLarguraEmMetros * 3.28084;
                                        //    larguraParam.Set(novaLarguraEmPes);
                                        //}

                                        //if (alturaParam != null)
                                        //{
                                        //    double novaAlturaEmMetros = 9;
                                        //    double novaAlturaEmPes = novaAlturaEmMetros * 3.28084;
                                        //    alturaParam.Set(novaAlturaEmPes);
                                        //}

                                        FamilyInstance customWindow = doc.Create.NewFamilyInstance(
                                          doorLocation,
                                          windowSymbol,
                                          wall,
                                          li,
                                          Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                    }
                                    else
                                    {

                                        XYZ doorLocation = new XYZ(map.x1 - map.x0, map.y0, -8);

                                        if (!windowSymbol.IsActive)
                                        {
                                            windowSymbol.Activate();
                                            doc.Regenerate();
                                        }

                                        //Parameter larguraParam = windowSymbol.get_Parameter(BuiltInParameter.WINDOW_WIDTH);
                                        //Parameter alturaParam = windowSymbol.get_Parameter(BuiltInParameter.WINDOW_HEIGHT);

                                        //if (larguraParam != null)
                                        //{
                                        //    double novaLarguraEmMetros = 9;
                                        //    double novaLarguraEmPes = novaLarguraEmMetros * 3.28084;
                                        //    larguraParam.Set(novaLarguraEmPes);
                                        //}

                                        //if (alturaParam != null)
                                        //{
                                        //    double novaAlturaEmMetros = 9;
                                        //    double novaAlturaEmPes = novaAlturaEmMetros * 3.28084;
                                        //    alturaParam.Set(novaAlturaEmPes);
                                        //}

                                        FamilyInstance customWindow = doc.Create.NewFamilyInstance(
                                          doorLocation,
                                          windowSymbol,
                                          wall,
                                          li,
                                          Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                    }



                                }

                            }


                        }




                    }
                    else
                    {
                        TaskDialog.Show("Erro", "The selected family have not FamilySymbols.");
                    }

                    FamilySymbol fs = f.GetFamilySymbolIds().Select(q => doc.GetElement(q)).Cast<FamilySymbol>().First(q => q.Name.StartsWith("09", StringComparison.InvariantCultureIgnoreCase));

                    // Reference r = uidoc.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element,"Pick point on wall");
                    // XYZ point = r.GlobalPoint;
                    // Element host = doc.GetElement(r);
                    // string elementAsString = $"Id: {host.Id}, Name: {host.Name}, Category: {host.Category.Name}";


                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (countErrorsWindow == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Window element. To do this, your window must contain the term \"Window\" in the descriptive name.");
                    }
                    countErrorsWindow++;
                    t.RollBack();
                }
            }
        }


        // Método para pegar um tipo de telhado (RoofType) válido
        private RoofType GetRoofType(Document doc)
        {
            // Coleta todos os tipos de telhado no projeto
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType));

            // Retorna o primeiro tipo de telhado encontrado
            return collector.FirstOrDefault() as RoofType;
        }

        public void CreateRoof(Document doc)
        { 
            RoofType roofType = GetRoofType(doc);

            XYZ startPoint = new XYZ(0, 0, 10);
            XYZ endPoint = new XYZ(20, 0, 10);

            Line baseCurve = Line.CreateBound(startPoint, endPoint);

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();

            XYZ extrusionDirection = new XYZ(0, 0, 10);


            double extrusionStart = 0.0;  
            double extrusionEnd = 10.0;

            XYZ point1 = new XYZ(399.5f / 304.8, 510 / 304.8, 0);
            XYZ point2 = new XYZ(399.5f / 304.8, 332 / 304.8, 0);
            XYZ point3 = new XYZ(603.5 / 304.8, 332 / 304.8, 0);
            XYZ point4 = new XYZ(603.5 / 304.8, 510 / 304.8, 0);

            Line line1 = Line.CreateBound(point1, point2); 
            Line line2 = Line.CreateBound(point2, point3);
            Line line3 = Line.CreateBound(point3, point4); 
            Line line4 = Line.CreateBound(point4, point1); 

            CurveArray curveArray = new CurveArray();
            curveArray.Append(line1);
            curveArray.Append(line2);
            curveArray.Append(line3);
            curveArray.Append(line4);

            XYZ referencePlaneOrigin = new XYZ((point1.X + point3.X) / 2,
                    (point1.Y + point3.Y) / 2,
                    li.Elevation);
            XYZ referencePlaneDirection1 = XYZ.BasisX;   
            XYZ referencePlaneDirection2 = XYZ.BasisZ;


            //XYZ normal = new XYZ(0, 0, 1); // Normal do plano (perpendicular ao plano XY)
            //XYZ origin = new XYZ(0, 0, 0); // Origem do plano, na posição Z = 0
            //Plane geometryPlane = Plane.CreateByNormalAndOrigin(normal, origin);
            //SketchPlane referencePlane = SketchPlane.Create(doc, geometryPlane);

            using (Transaction t = new Transaction(doc, "Create Roof"))
                {
                    try
                    {
                        t.Start();




                    Plane geometryPlane = Plane.CreateByNormalAndOrigin(
                            XYZ.BasisZ, // Normal do plano (Z para horizontal)
                            new XYZ(0, 0, li.Elevation));

                    SketchPlane sketchPlane = SketchPlane.Create(doc, geometryPlane);
                    ReferencePlane referencePlane = doc.Create.NewReferencePlane(referencePlaneOrigin,
                    referencePlaneDirection1,
                    referencePlaneDirection2, doc.ActiveView);

                    RoofBase roof = doc.Create.NewExtrusionRoof(curveArray, referencePlane, li, roofType, extrusionStart, extrusionEnd);

                        TaskDialog.Show("Sucesso", "Telhado Extrusionado criado com sucesso.");


                        t.Commit();

                    }
                    catch (InvalidOperationException ex)
                    {
                        // Tratar exceção específica de operação inválida (ex: operação ilegal no contexto)
                        TaskDialog.Show("Erro", "Erro ao criar o telhado: " + ex.Message);
                    }
                    catch (ArgumentNullException ex)
                    {
                        // Tratar exceção específica de parâmetro nulo
                        TaskDialog.Show("Erro", "Argumento nulo: " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        // Tratar outras exceções gerais
                        TaskDialog.Show("Erro", "Ocorreu um erro inesperado: " + ex.Message);
                    }
            }
        }



        public void RequestData(ExternalCommandData commandData, string userId)
        {
            var handler = new System.Net.Http.HttpClientHandler();

            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            var walls = new List<Prediction>();
            var diagonalWalls = new List<Prediction>();
            var wallsWithCurveRight = new List<Prediction>();
            var wallsWithCurveLeft = new List<Prediction>();
            var wallsWithCurveUp = new List<Prediction>();
            var wallsWithCurveDown = new List<Prediction>();
            var doors = new List<Prediction>();
            var windows = new List<Prediction>();
            var structures = new List<Prediction>();

            using (var client = new System.Net.Http.HttpClient(handler))
            {

                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                client.DefaultRequestHeaders.Add("Authorization", "f3535709ff6f86c24e506d8be7cba4f02dd6590e5467a88bc002f725cddb5c6b");


                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://empoli-model-api-719774307855.us-central1.run.app/homolog?userId=" + userId);

                var response = client.SendAsync(request).GetAwaiter().GetResult();

                var responseBody = response.Content.ReadAsStringAsync().Result;

                if (responseBody == null)
                {
                    return;
                }

                ResponseAPI searchResponse = JsonConvert.DeserializeObject<ResponseAPI>(responseBody);


                if (searchResponse?.walls == null)
                {
                    return;
                }


                foreach (Prediction item in searchResponse.walls)
                {

                    if (item.@class == "Wall")
                    {

                        var newWall = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "Wall",
                            points = new List<Point>(),
                            has_window = item.has_window,
                            has_door = item.has_door,
                            children = item.children
                        };

                        walls.Add(newWall);

                    }


                    if (item.@class == "diagonal")
                    {

                        var newDiagonalWall = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "diagonal",
                            points = item.points,
                            has_window = item.has_window,
                            has_door = item.has_door,
                            children = item.children
                        };

                        diagonalWalls.Add(newDiagonalWall);

                    }





                    if (item.@class == "CurveRight")
                    {

                        var newWallWithCurveRight = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "CurveRight",
                            points = item.points,
                            has_window = item.has_window,
                            has_door = item.has_door,
                            children = item.children
                        };

                        wallsWithCurveRight.Add(newWallWithCurveRight);

                    }


                    if (item.@class == "CurveLeft")
                    {

                        var newWallWithCurveLeft = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "CurveLeft",
                            points = item.points,
                            has_window = item.has_window,
                            has_door = item.has_door,
                            children = item.children
                        };

                        wallsWithCurveLeft.Add(newWallWithCurveLeft);

                    }

                    if (item.@class == "CurveUp")
                    {

                        var newWallWithCurveUp = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "CurveUp",
                            points = item.points,
                            has_window = item.has_window,
                            has_door = item.has_door,
                            children = item.children
                        };

                        wallsWithCurveUp.Add(newWallWithCurveUp);

                    }

                    if (item.@class == "CurveDown")
                    {

                        var newWallWithCurveDown = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "CurveUp",
                            points = item.points,
                            has_window = item.has_window,
                            has_door = item.has_door,
                            children = item.children
                        };

                        wallsWithCurveDown.Add(newWallWithCurveDown);

                    }



                    if (item.@class == "Door")
                    {

                        var newDoor = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "Door",
                            points = new List<Point>()
                        };

                        doors.Add(newDoor);

                    }


                    if (item.@class == "Window")
                    {

                        var newWindow = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "Window",
                            points = new List<Point>()
                        };

                        windows.Add(newWindow);

                    }

                    if (item.@class == "Structure")
                    {

                        var newStructure = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "Structure",
                        };

                        structures.Add(newStructure);

                    }


                }

            }

            var handler1 = new System.Net.Http.HttpClientHandler();


            var newWalls = new List<Prediction>();

            using (var client2 = new System.Net.Http.HttpClient(handler1))
            {

                client2.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");


                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.empoli.ai/project/list-historic?userId=" + userId);

                var response = client2.SendAsync(request).GetAwaiter().GetResult();

                var responseBody = response.Content.ReadAsStringAsync().Result;

                if (responseBody == null)
                {
                    return;
                }

                ResponseAPI searchResponse = JsonConvert.DeserializeObject<ResponseAPI>(responseBody);

                string responseString = JsonConvert.SerializeObject(searchResponse, Formatting.Indented);

                //TaskDialog.Show("Teste response", responseString);

                var index = 0;

                if (searchResponse.walls.Count > 0) { 

                    foreach (Prediction item in walls)
                    {

                        if (item.@class == "Wall")
                        {

                            var hasValueInX = searchResponse.walls[index].x != 0;
                            var hasValueInY = searchResponse.walls[index].y != 0;
                            var hasValueInWidth = searchResponse.walls[index].width != 0;
                            var hasValueInHeight = searchResponse.walls[index].height != 0;

                            var customWall = new Prediction
                            {
                                x = hasValueInX ? searchResponse.walls[index].x : item.x,
                                y = hasValueInY ? searchResponse.walls[index].y : item.y,
                                width = hasValueInWidth ? searchResponse.walls[index].width : item.width,
                                height = hasValueInHeight ? searchResponse.walls[index].height : item.height,
                                @class = "Wall",
                                points = new List<Point>(),
                                has_window = item.has_window,
                                has_door = item.has_door,
                                children = item.children
                            };

                            newWalls.Add(customWall);

                        }

                        index++;
                    }


                }
            }

            if (newWalls.Count > 0)
            {
                CreatingWall(newWalls, commandData);
            } else
            {
                CreatingWall(walls, commandData);
            }

            CreatingDiagonalWall(diagonalWalls, commandData);

            CreatingWallWithCurvesRight(wallsWithCurveRight, commandData);
            CreatingWallWithCurvesLeft(wallsWithCurveLeft, commandData);
            CreatingWallWithCurvesUp(wallsWithCurveUp, commandData);
            CreatingWallWithCurvesDown(wallsWithCurveDown, commandData);






            foreach (Prediction item in walls)
            {
                // TaskDialog.Show("===>", $"x: {item.x}, y: {item.y}, width: {item.width}, height: {item.height}");
            }


            // if (response.IsSuccessStatusCode)
            // {    

            // string responseBody = await response.Content.ReadAsStringAsync();

            // ResponseAPI searchResponse = JsonConvert.DeserializeObject<ResponseAPI>(responseBody);

            // string result = await response.Content.ReadAsStringAsync();
            // Console.WriteLine("Wall created: " + result);
            // TaskDialog.Show("title", searchResponse.total_count.ToString());
            // }
            // else
            // {
            //    TaskDialog.Show("title","errou");
            // Console.WriteLine("Failed to create wall: " + response.StatusCode);
            // }

        }




        public void RequestDataFurniture(ExternalCommandData commandData, string userId)
        {
            var handler = new System.Net.Http.HttpClientHandler();

            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            var sofa = new List<Prediction>();
            var ssofa = new List<Prediction>();
            var chair = new List<Prediction>();
            var toilet = new List<Prediction>();
            var bathtub = new List<Prediction>();
            var table = new List<Prediction>();
            var sink = new List<Prediction>();
            var bed = new List<Prediction>();

            using (var client = new System.Net.Http.HttpClient(handler))
            {

                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                client.DefaultRequestHeaders.Add("Authorization", "TESTE1234");


                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://ai-projects-436718.ue.r.appspot.com/homolog-furniture2?userId=" + userId);

                var response = client.SendAsync(request).GetAwaiter().GetResult();

                var responseBody = response.Content.ReadAsStringAsync().Result;

                if (responseBody == null)
                {
                    return;
                }

                ResponseFurnitureAPI searchResponse = JsonConvert.DeserializeObject<ResponseFurnitureAPI>(responseBody);

                if (searchResponse?.predictions == null)
                {
                    return;
                }



                foreach (PredictionFurniture item in searchResponse.predictions)
                {

                    if (item.@class == "sofa")
                    {

                        var newSofa = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "sofa",
                        };

                        sofa.Add(newSofa);

                    }


                    if (item.@class == "ssofa" || item.@class == "sofa")
                    {

                        var newSsofa = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "ssofa",
                        };

                        ssofa.Add(newSsofa);

                    }



                    if (item.@class == "toilet")
                    {

                        var newToilet = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "toilet",
                        };

                        toilet.Add(newToilet);

                    }



                    if (item.@class == "bathtub")
                    {

                        var newBathtub = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "bathtub",
                        };

                        bathtub.Add(newBathtub);

                    }


                    if (item.@class == "dtable" || item.@class == "table")
                    {

                        var newTable = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "dtable",
                        };

                        table.Add(newTable);

                    }


                    if (item.@class == "sink")
                    {

                        var newSink = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "sink",
                        };

                        sink.Add(newSink);

                    }


                    if (item.@class == "chair")
                    {

                        var newChair = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "chair",
                        };

                        chair.Add(newChair);

                    }

                    if (item.@class == "bed")
                    {

                        var newBed = new Prediction
                        {
                            x = item.x,
                            y = item.y,
                            width = item.width,
                            height = item.height,
                            @class = "bed",
                        };

                        bed.Add(newBed);

                    }


                }

            }



            CreatingSsofa(ssofa, commandData);
            CreatingToilet(toilet, commandData);
            CreatingBathtub(bathtub, commandData);
            CreatingTable(table, commandData);
            CreatingSink(sink, commandData);
            CreatingChair(chair, commandData);
            CreatingBed(bed, commandData);

        }



        public void CreatingWall(List<Prediction> walls, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Line l1 = Line.CreateBound(XYZ.Zero, new XYZ(60, 0, 0));
            // Line l2 = Line.CreateBound(XYZ.Zero, new XYZ(0, 60, 0));
            // Line l3 = Line.CreateBound(XYZ.Zero, new XYZ(0, -60, 0));
            // Line l4 = Line.CreateBound(new XYZ(60, 0, 0), new XYZ(60, 60, 0));
            // XYZ doorLocation = new XYZ(-2, 9, -15); 
            // Line l5 = Line.CreateBound(new XYZ(60, -60, 0), new XYZ(60, 0, 0));
            // Line l6 = Line.CreateBound(new XYZ(0, 60, 0), new XYZ(60, 60, 0));
            // Line l7 = Line.CreateBound(new XYZ(0, -60, 0), new XYZ(60, -60, 0));

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();

            // Task.Run(() => RequestData()).Wait();  
            //  HttpClient client = new HttpClient();

            // HttpResponseMessage response = client.Get("https://api.github.com/search/repositories?q=Q");



            // if (response.IsSuccessStatusCode)
            // {    

            //    string responseBody = await response.Content.ReadAsStringAsync();

            //    ResponseAPI searchResponse = JsonConvert.DeserializeObject<ResponseAPI>(responseBody);

            //    string result = await response.Content.ReadAsStringAsync();
            //    Console.WriteLine("Wall created: " + result);
            //    TaskDialog.Show("title", searchResponse.total_count.ToString());
            // }
            // else
            // {
            //    Console.WriteLine("Failed to create wall: " + response.StatusCode);
            // }

            List<Wall> paredes = new List<Wall>();

            List<Map> maps = new List<Map>();

            WallType wallType = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(wt  =>wt.Name.Contains("W"))
            .First();

            var countErrors = 0;


            using (Transaction t = new Transaction(doc, "Create Wall"))
            {
                try
                {
                    t.Start();

                    foreach (Prediction item in walls)
                    {

                        // x0, y0  x1, y1
                        if (item.width >= item.height)
                        {


                            //TaskDialog.Show("x0", (item.x - item.width / 2).ToString());
                            //TaskDialog.Show("y0", (item.y).ToString());
                            //TaskDialog.Show("x1", (item.x + item.width / 2).ToString());
                            //TaskDialog.Show("y1", (item.y).ToString());

                            Line line = Line.CreateBound(new XYZ(item.x - item.width / 2, item.y, 0), new XYZ(item.x + item.width / 2, item.y, 0));

                                IList<Curve> curves = new List<Curve> { line };
                                double heightInFeet = 5.0 * 3.28084;
                                double offset = 1.0;
                                bool flip = false;

                                Wall parede = Wall.Create(doc, line, wallType.Id, li.Id, heightInFeet, offset, flip, false);

                                var map = new Map
                                {
                                    x0 = item.x - item.width / 2,
                                    y0 = item.y,
                                    x1 = item.x + item.width / 2,
                                    y1 = item.y
                                };

                                maps.Add(map);

                                paredes.Add(parede);
                            }

                        else
                        {


                            //TaskDialog.Show("x0", (item.x).ToString());
                            //TaskDialog.Show("y0", (item.y - item.height / 2).ToString());
                            //TaskDialog.Show("x1", (item.x).ToString());
                            //TaskDialog.Show("y1", (item.y + item.height / 2).ToString());


                            Line line = Line.CreateBound(new XYZ(item.x, item.y - item.height / 2, 0), new XYZ(item.x, item.y + item.height / 2, 0));

                                IList<Curve> curves = new List<Curve> { line };
                                double heightInFeet = 5.0 * 3.28084;
                                double offset = 1.0;
                                bool flip = false;

                                Wall parede = Wall.Create(doc, line, wallType.Id, li.Id, heightInFeet, offset, flip, false);

                                paredes.Add(parede);


                                var map = new Map
                                {
                                    x0 = item.x,
                                    y0 = item.y - item.height / 2,
                                    x1 = item.x,
                                    y1 = item.y + item.height / 2
                                };

                                maps.Add(map);
                        }
                    }

                    // var predictionsList = new List<Prediction>
                    // {
                    //       new Prediction
                    //       {
                    //          x = 491, 
                    //          y = 256,
                    //          width = 491,
                    //          height = 331,
                    //          confidence = 0.924f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "7d19777a-09fb-41a9-b9d3-3f9143c141a0",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 344, y = 102 },
                    //             new Point { x = 344, y = 106 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 366,
                    //          y = 252,
                    //          width = 366,
                    //          height = 257,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 404,
                    //          y =  330,
                    //          width = 404,
                    //          height = 331,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 37,
                    //          y = 168,
                    //          width = 37,
                    //          height = 173,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 209,
                    //          y = 253,
                    //          width = 209,
                    //          height = 291,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 211,
                    //          y = 252,
                    //          width = 211,
                    //          height = 254,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 325,
                    //          y = 261,
                    //          width = 325,
                    //          height = 421.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 325,
                    //          y = 279.91f,
                    //          width = 491,
                    //          height = 279.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 325,
                    //          y = 91.91f,
                    //          width = 325,
                    //          height = 213.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 176,
                    //          y = 204.91f,
                    //          width = 176,
                    //          height = 238.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },

                    // };


                    // var predictionsList = new List<Prediction>
                    // {
                    //       new Prediction
                    //       {
                    //          x = 30, 
                    //          y = 141.91f,
                    //          width = 170,
                    //          height = 141.91f,
                    //          confidence = 0.924f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "7d19777a-09fb-41a9-b9d3-3f9143c141a0",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 344, y = 102 },
                    //             new Point { x = 344, y = 106 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 169,
                    //          y = 84,
                    //          width = 169,
                    //          height = 170,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 28,
                    //          y =  138.91f,
                    //          width = 28,
                    //          height = 277.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 28,
                    //          y = 277.91f,
                    //          width = 177,
                    //          height =  277.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 135,
                    //          y = 278.91f,
                    //          width = 135,
                    //          height = 373.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 135,
                    //          y = 375.91f,
                    //          width = 326,
                    //          height = 375.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 325,
                    //          y = 261,
                    //          width = 325,
                    //          height = 421.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 325,
                    //          y = 279.91f,
                    //          width = 491,
                    //          height = 279.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 325,
                    //          y = 91.91f,
                    //          width = 325,
                    //          height = 213.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },
                    //       new Prediction
                    //       {
                    //          x = 176,
                    //          y = 204.91f,
                    //          width = 176,
                    //          height = 238.91f,
                    //          confidence = 0.907f,
                    //          @class = "Wall",
                    //          class_id = 2,
                    //          detection_id = "76b043fc-2291-4eee-a829-9bc2e12dd90f",
                    //          points = new List<Point>
                    //          {
                    //             new Point { x = 159, y = 329 },
                    //             new Point { x = 159, y = 334 },
                    //          }
                    //       },

                    // };


                    // Wall w1 = Wall.Create(doc, l1, li.Id, false);
                    // Wall w2 =  Wall.Create(doc, l2, li.Id, false);
                    // Wall w3 = Wall.Create(doc, l3, li.Id, false);
                    // Wall w4 = Wall.Create(doc, l4, li.Id, false);
                    // Wall w5 = Wall.Create(doc, l5, li.Id, false);
                    // Wall w6 =  Wall.Create(doc, l6, li.Id, false);
                    // Wall w7 = Wall.Create(doc, l7, li.Id, false);

                    // XYZ doorLocation = new XYZ(-2, 9, -15); 

                    // FamilySymbol doorType = GetDoorType(doc);
                    // if (doorType != null && !doorType.IsActive)
                    // {
                    //    doorType.Activate();
                    // }

                    // Family f = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                    //    .FirstOrDefault(q => q.FamilyCategoryId.IntegerValue == (int)BuiltInCategory.OST_Doors
                    //                  ) as Family;

                    // List<string> doorSymbols = new List<string>();

                    // doorSymbols.Add(f.Name);

                    // string result = string.Join(", ", doorSymbols);

                    // if (f == null)
                    // {
                    //    TaskDialog.Show("Error", "no family");
                    //    return;	
                    // }


                    // FamilySymbol fs = f.GetFamilySymbolIds().Select(q => doc.GetElement(q)).Cast<FamilySymbol>().First(q => q.Name.StartsWith("0915", StringComparison.InvariantCultureIgnoreCase));

                    // Reference r = uidoc.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element,"Pick point on wall");
                    // XYZ point = r.GlobalPoint;
                    // Element host = doc.GetElement(r);
                    // string elementAsString = $"Id: {host.Id}, Name: {host.Name}, Category: {host.Category.Name}";



                    //  FamilyInstance door = doc.Create.NewFamilyInstance(
                    //    doorLocation, 
                    //    fs, 
                    //    w4,
                    //    li,
                    //    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);


                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (countErrors != 0 )
                    {
                        TaskDialog.Show("Alert", "Our code uses user resources to create the Wall element. To do this, your wall must contain the term \"W\" in the descriptive name.");

                    }
                    countErrors++;
                    t.RollBack();
                }
            }


            int index = 0;
            int countErrorsWindow = 0;


            foreach (Wall parede in paredes)
            {

                if (walls[index].has_window)
                {
                    CreateWindow(parede, commandData, walls[index], maps[index], li, ref countErrorsWindow);
                }
                index++;
            }


            int indexDoor = 0;

            int countErrorsDoor = 0;

            foreach (Wall parede in paredes)
            {

                if (walls[indexDoor].has_door)
                {
                    CreateDoor(parede, commandData, walls[indexDoor], maps[indexDoor], li, ref countErrorsDoor);
                }
                indexDoor++;
            }


            //CreateRoof(doc);

        }


        public void CreatingDiagonalWall(List<Prediction> walls, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            List<Wall> paredes = new List<Wall>();

            List<Map> maps = new List<Map>();

            WallType wallType = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(wt => wt.Name.Contains("W"))
            .First();

            var countErrors = 0;
            //TaskDialog.Show("aqui.x", "diagonal");

            using (Transaction t = new Transaction(doc, "Create Diagonal Wall"))
            {
                try
                {
                    t.Start();

                    foreach (Prediction item in walls)
                    {
                        //TaskDialog.Show("foreaCH.x", "ENTROU" + item.points.ToString());
                        var minorY = item.points.Min(o => o.y);
                        var objetoMinorY = item.points.First(o => o.y == minorY);

                        var majorY = item.points.Max(o => o.y);
                        var objetoMajorY = item.points.First(o => o.y == majorY);

                        //TaskDialog.Show("objetoMinorY.x", objetoMinorY.x.ToString());
                        //TaskDialog.Show("objetoMinorY.y", objetoMinorY.y.ToString());
                        //TaskDialog.Show("objetoMajorY.x", objetoMajorY.x.ToString());
                        //TaskDialog.Show("objetoMajorY.y", objetoMajorY.y.ToString());


                        XYZ startPoint = new XYZ(objetoMinorY.x, objetoMinorY.y, 0);
                        XYZ endPoint = new XYZ(objetoMajorY.x, objetoMajorY.y, 0);

                        Line line = Line.CreateBound(startPoint, endPoint);

                        IList<Curve> curves = new List<Curve> { line };
                        double heightInFeet = 5.0 * 3.28084;
                        double offset = 1.0;
                        bool flip = false;

                        Wall parede = Wall.Create(doc, line, wallType.Id, li.Id, heightInFeet, offset, flip, false);

                        var map = new Map
                        {
                            x0 = objetoMinorY.x,
                            y0 = objetoMinorY.y,
                            x1 = objetoMajorY.x,
                            y1 = objetoMajorY.y
                        };

                        maps.Add(map);

                        paredes.Add(parede);
                    }



                    t.Commit();
                }
                catch (Exception ex)
                {

                    if (countErrors != 0)
                    {
                        TaskDialog.Show("Alert", "Our code uses user resources to create the Wall element. To do this, your wall must contain the term \"W\" in the descriptive name.");

                    }
                    countErrors++;
                    t.RollBack();
                }
            }


            int index = 0;

            int countErrorsWindow = 0;

            foreach (Wall parede in paredes)
            {

                if (walls[index].has_window)
                {
                    CreateWindow(parede, commandData, walls[index], maps[index], li, ref countErrorsWindow);
                }
                index++;
            }


            int indexDoor = 0;

            int countErrorsDoor = 0;

            foreach (Wall parede in paredes)
            {

                if (walls[indexDoor].has_door)
                {
                    CreateDoor(parede, commandData, walls[indexDoor], maps[indexDoor], li, ref countErrorsDoor);
                }
                indexDoor++;
            }


        }



        public void CreatingWallWithCurvesRight(List<Prediction> walls, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();

  

            List<Wall> paredes = new List<Wall>();

            List<Map> maps = new List<Map>();

            WallType wallType = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(wt => wt.Name.Contains("W"))
            .First();

            var countErrors = 0;


            using (Transaction t = new Transaction(doc, "Create Wall With Curve Right"))
            {
                try
                {
                    t.Start();

                    foreach (Prediction item in walls)
                    {


                        var minorY = item.points.Min(o => o.y);
                        var objetoMinorY = item.points.First(o => o.y == minorY);

                        var majorY = item.points.Max(o => o.y);
                        var objetoMajorY = item.points.First(o => o.y  == majorY);

                        var mediaY = item.points.Average(o => o.y);
                        var objetoMediaY = item.points.OrderBy(o => Math.Abs(o.y - mediaY)).First();


                        XYZ startPoint = new XYZ(objetoMinorY.x, objetoMinorY.y, 0);
                        XYZ tangentPoint = new XYZ(objetoMediaY.x, objetoMediaY.y, 0);
                        XYZ endPoint = new XYZ(objetoMajorY.x, objetoMajorY.y, 0);


                        Curve arco = Arc.Create(startPoint, endPoint, tangentPoint);

                        Wall parede = Wall.Create(doc, arco, li.Id, false);


                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (countErrors != 0)
                    {
                        TaskDialog.Show("Alert", "Our code uses user resources to create the Wall element. To do this, your wall must contain the term \"W\" in the descriptive name.");

                    }
                    countErrors++;
                    t.RollBack();
                }
            }

        }


        public void CreatingWallWithCurvesLeft(List<Prediction> walls, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();



            List<Wall> paredes = new List<Wall>();

            List<Map> maps = new List<Map>();

            WallType wallType = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(wt => wt.Name.Contains("W"))
            .First();

            var countErrors = 0;


            using (Transaction t = new Transaction(doc, "Create Wall With Curve Left"))
            {
                try
                {
                    t.Start();

                    foreach (Prediction item in walls)
                    {

                        var minorY = item.points.Min(o => o.y);
                        var objetoMinorY = item.points.First(o => o.y == minorY);

                        var majorY = item.points.Max(o => o.y);
                        var objetoMajorY = item.points.First(o => o.y == majorY);

                        var mediaY = item.points.Average(o => o.y);
                        var objetoMediaY = item.points.OrderBy(o => Math.Abs(o.y - mediaY)).First();


                        XYZ startPoint = new XYZ(objetoMinorY.x, objetoMinorY.y, 0);
                        XYZ tangentPoint = new XYZ(objetoMediaY.x, objetoMediaY.y, 0);
                        XYZ endPoint = new XYZ(objetoMajorY.x, objetoMajorY.y, 0);


                        Curve arco = Arc.Create(startPoint, endPoint, tangentPoint);

                        Wall parede = Wall.Create(doc, arco, li.Id, false);


                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Wall element. To do this, your wall must contain the term \"W\" in the descriptive name.");
                    }

                    countErrors++;
                    t.RollBack();
                }
            }

        }


        public void CreatingWallWithCurvesUp(List<Prediction> walls, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();



            List<Wall> paredes = new List<Wall>();

            List<Map> maps = new List<Map>();

            WallType wallType = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(wt => wt.Name.Contains("W"))
            .First();

            var countErrors = 0;


            using (Transaction t = new Transaction(doc, "Create Wall With Curve Up"))
            {
                try
                {
                    t.Start();

                    foreach (Prediction item in walls)
                    {

                        var minorX = item.points.Min(o => o.x);
                        var objetoMinorX = item.points.First(o => o.x == minorX);

                        var majorX = item.points.Max(o => o.x);
                        var objetoMajorX = item.points.First(o => o.x == majorX);

                        var mediaX = item.points.Average(o => o.x);
                        var objetoMediaX = item.points.OrderBy(o => Math.Abs(o.x - mediaX)).First();


                        XYZ startPoint = new XYZ(objetoMinorX.x, objetoMinorX.y, 0);
                        XYZ tangentPoint = new XYZ(objetoMediaX.x, objetoMediaX.y, 0);
                        XYZ endPoint = new XYZ(objetoMajorX.x, objetoMajorX.y, 0);


                        Curve arco = Arc.Create(startPoint, endPoint, tangentPoint);

                        Wall parede = Wall.Create(doc, arco, li.Id, false);


                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Wall element. To do this, your wall must contain the term \"W\" in the descriptive name.");
                    }

                    countErrors++;
                    t.RollBack();
                }
            }

        }


        public void CreatingWallWithCurvesDown(List<Prediction> walls, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();



            List<Wall> paredes = new List<Wall>();

            List<Map> maps = new List<Map>();

            WallType wallType = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(wt => wt.Name.Contains("W"))
            .First();

            var countErrors = 0;


            using (Transaction t = new Transaction(doc, "Create Wall With Curve Up"))
            {
                try
                {
                    t.Start();

                    foreach (Prediction item in walls)
                    {

                        var minorX = item.points.Min(o => o.x);
                        var objetoMinorX = item.points.First(o => o.x == minorX);

                        var majorX = item.points.Max(o => o.x);
                        var objetoMajorX = item.points.First(o => o.x == majorX);

                        var mediaX = item.points.Average(o => o.x);
                        var objetoMediaX = item.points.OrderBy(o => Math.Abs(o.x - mediaX)).First();


                        XYZ startPoint = new XYZ(objetoMinorX.x, objetoMinorX.y, 0);
                        XYZ tangentPoint = new XYZ(objetoMediaX.x, objetoMediaX.y, 0);
                        XYZ endPoint = new XYZ(objetoMajorX.x, objetoMajorX.y, 0);


                        Curve arco = Arc.Create(startPoint, endPoint, tangentPoint);

                        Wall parede = Wall.Create(doc, arco, li.Id, false);


                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Wall element. To do this, your wall must contain the term \"W\" in the descriptive name.");
                    }

                    countErrors++;
                    t.RollBack();
                }
            }

        }



        public void CreatingWallWithCurves(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            WallType wallType = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(wt => wt.Name.Contains("W"))
            .First();


            using (Transaction t = new Transaction(doc, "Create Wall with curve"))
            {
                try
                {
                    t.Start();

                    XYZ startPoint = new XYZ(0, 0, 0);       
                    XYZ endPoint = new XYZ(10, 10, 0);     
                    XYZ tangentPoint = new XYZ(5, 15, 0);  

                    Curve arco = Arc.Create(startPoint, endPoint, tangentPoint);

                    Wall parede = Wall.Create(doc, arco, li.Id, false);



                    t.Commit();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Alert", "Our code uses user resources to create the Wall element. To do this, your wall must contain the term \"W\" in the descriptive name.");
                    t.RollBack();
                }
            }


        }

        public void CreateSsofa(Prediction item, ExternalCommandData commandData, FamilySymbol familyType, ref int countErrors)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            XYZ position = new XYZ(item.x, item.y, 0);


            using (Transaction trans = new Transaction(doc, "Criar Sofá"))
            {
                try
                {
                    trans.Start();
                    if (!familyType.IsActive)
                    {
                        familyType.Activate();
                        doc.Regenerate();
                    }

                    FamilyInstance instancia = doc.Create.NewFamilyInstance(position, familyType, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);


                    trans.Commit();
                } 
                catch (Exception ex)
                {
                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Sofa element. To do this, your sofa must contain the term \"Sofa\" in the descriptive name.");
                    }

                    countErrors++;
                    trans.RollBack();
                }
            }


        }


        public void CreateSanitary(Prediction item, ExternalCommandData commandData, FamilySymbol familyType, ref int countErrors)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            XYZ position = new XYZ(item.x, item.y, 0);

            using (Transaction trans = new Transaction(doc, "Criar Sanitary"))
            {

                try
                {
                    trans.Start();
                    if (!familyType.IsActive)
                    {
                        familyType.Activate();
                        doc.Regenerate();
                    }

                    FamilyInstance instancia = doc.Create.NewFamilyInstance(position, familyType, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);



                    trans.Commit();
                }
                catch (Exception ex)
                {

                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Sanitary element. To do this, your sanitary must contain the term \"Vitreous\" in the descriptive name.");
                    }
                    countErrors++;
                    trans.RollBack();
                }
            }


        }


        public void CreateBathtub(Prediction item, ExternalCommandData commandData, FamilySymbol familyType, ref int countErrors)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            XYZ position = new XYZ(item.x, item.y, 0);

            //TaskDialog.Show("Teste", "Bath ID" + familyType.Id);
            //TaskDialog.Show("Teste", "item.x" + item.x);
            //TaskDialog.Show("Teste", "item.y" + item.y);


            using (Transaction trans = new Transaction(doc, "Criar Bathtub"))
            {
                try
                {
                    trans.Start();
                    if (!familyType.IsActive)
                    {
                        familyType.Activate();
                        doc.Regenerate();
                    }

                    FamilyInstance instancia = doc.Create.NewFamilyInstance(position, familyType, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);



                    trans.Commit();

                }
                catch (Exception ex)
                {
                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Bathtub element. To do this, your bathtub must contain the term \"FS1\" in the descriptive name.");
                    }
                    countErrors++;
                    trans.RollBack();
                }
            }

        }


        public void CreateTable(Prediction item, ExternalCommandData commandData, FamilySymbol familyType, ref int countErrors)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            XYZ position = new XYZ(item.x, item.y, 0);

            //TaskDialog.Show("Teste", "Bath ID" + familyType.Id);
            //TaskDialog.Show("Teste", "item.x" + item.x);
            //TaskDialog.Show("Teste", "item.y" + item.y);

            using (Transaction trans = new Transaction(doc, "Criar Table"))
            {
                try
                {
                    trans.Start();
                    if (!familyType.IsActive)
                    {
                        familyType.Activate();
                        doc.Regenerate();
                    }

                    FamilyInstance instancia = doc.Create.NewFamilyInstance(position, familyType, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);



                    trans.Commit();


                }
                catch (Exception ex)
                {

                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Table element. To do this, your table must contain the term \"Meyer\" in the descriptive name.");
                    }
                    countErrors++;
                    trans.RollBack();
                }
            }

        }


        public void CreateSink(Prediction item, ExternalCommandData commandData, FamilySymbol familyType, ref int countErrors)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            XYZ position = new XYZ(item.x, item.y, 0);

            //TaskDialog.Show("Teste", "Bath ID" + familyType.Id);
            //TaskDialog.Show("Teste", "item.x" + item.x);
            //TaskDialog.Show("Teste", "item.y" + item.y);


            using (Transaction trans = new Transaction(doc, "Criar Sink"))
            {
                try
                {
                    trans.Start();
                    if (!familyType.IsActive)
                    {
                        familyType.Activate();
                        doc.Regenerate();
                    }

                    FamilyInstance instancia = doc.Create.NewFamilyInstance(position, familyType, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);



                    trans.Commit();
                }
                catch (Exception ex)
                {

                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Sink element. To do this, your sink must contain the term \"Sink\" in the descriptive name.");
                    }
                    countErrors++;
                    trans.RollBack();
                }
            }

        }

        public void CreateBed(Prediction item, ExternalCommandData commandData, FamilySymbol familyType, ref int countErrors)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            XYZ position = new XYZ(item.x, item.y, 0);

            //TaskDialog.Show("Teste", "Bath ID" + familyType.Id);
            //TaskDialog.Show("Teste", "item.x" + item.x);
            //TaskDialog.Show("Teste", "item.y" + item.y);



            using (Transaction trans = new Transaction(doc, "Criar Bed"))
            {
                try
                {
                    trans.Start();
                    if (!familyType.IsActive)
                    {
                        familyType.Activate();
                        doc.Regenerate();
                    }

                    FamilyInstance instancia = doc.Create.NewFamilyInstance(position, familyType, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);



                    trans.Commit();
                }
                catch (Exception ex)
                {
                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Bed element. To do this, your bed must contain the term \"Bed\" in the descriptive name.");
                    }
                    countErrors++;
                    trans.RollBack();
                }
            }

        }


        public void CreateChair(Prediction item, ExternalCommandData commandData, FamilySymbol familyType, ref int countErrors)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            XYZ position = new XYZ(item.x, item.y, 0);


            using (Transaction trans = new Transaction(doc, "Criar Chair"))
            {
                try
                {
                    trans.Start();
                    if (!familyType.IsActive)
                    {
                        familyType.Activate();
                        doc.Regenerate();
                    }

                    FamilyInstance instancia = doc.Create.NewFamilyInstance(position, familyType, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);



                    trans.Commit();
                }
                catch (Exception ex)
                {

                    if (countErrors == 0)
                    {

                        TaskDialog.Show("Alert", "Our code uses user resources to create the Chair element. To do this, your chair must contain the term \"Chair\" in the descriptive name.");
                    }
                    countErrors++;
                    trans.RollBack();
                }
            }

        }


        public void CreatingToilet(List<Prediction> sofas, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Family));

            List<string> familyNames = new List<string>();


            string key = "Vitreous";
            var customSanitary = collector
                .Cast<Family>()
                .Where(f => f.Name.Contains(key))
                .ToList();



            foreach (Family instance in customSanitary)
            {
                ScaleFamilyInstance(doc, instance, 40);
            }

            FamilySymbol familyType = null;


            if (customSanitary != null && customSanitary.Count > 0 && customSanitary[0] != null)
            {

                foreach (ElementId id in customSanitary[0].GetFamilySymbolIds())
                {
                    familyType = doc.GetElement(id) as FamilySymbol;
                    break;
                }
            }

            string resultado = string.Join("\n", familyNames);


            var countErrors = 0;

            foreach (var item in sofas)
            {
                CreateSanitary(item, commandData, familyType, ref countErrors);
            }
        }


        public void CreatingBathtub(List<Prediction> bathtubs, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Family));

            List<string> familyNames = new List<string>();


            string key = "FS1";
            var customBathtub = collector
                .Cast<Family>()
                .Where(f => f.Name.Contains(key))
                .ToList();



            foreach (Family instance in customBathtub)
            {
                ScaleFamilyInstance(doc, instance, 15);
            }

            FamilySymbol familyType = null;


            if (customBathtub != null && customBathtub.Count > 0 && customBathtub[0] != null)
            {

                foreach (ElementId id in customBathtub[0].GetFamilySymbolIds())
                {
                    familyType = doc.GetElement(id) as FamilySymbol;
                    break;
                }

            }

            var countErrors = 0;
            string resultado = string.Join("\n", familyNames);

            foreach (var item in bathtubs)
            {
                CreateBathtub(item, commandData, familyType, ref countErrors);
            }
        }


        public void CreatingTable(List<Prediction> tables, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Family));

            List<string> familyNames = new List<string>();


            string key = "Meyer";
            var customTable = collector
                .Cast<Family>()
                .Where(f => f.Name.Contains(key))
                .ToList();



            foreach (Family instance in customTable)
            {
                ScaleFamilyInstance(doc, instance, 15);
            }

            FamilySymbol familyType = null;

            if (customTable != null && customTable.Count > 0 && customTable[0] != null)
            {

                foreach (ElementId id in customTable[0].GetFamilySymbolIds())
                {
                    familyType = doc.GetElement(id) as FamilySymbol;
                    break;
                }
            }


            string resultado = string.Join("\n", familyNames);
            var countErrors = 0;

            foreach (var item in tables)
            {
                CreateTable(item, commandData, familyType, ref countErrors);
            }
        }


        public void CreatingSink(List<Prediction> sinks, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Family));

            List<string> familyNames = new List<string>();


            string key = "Sink";
            var customSink = collector
                .Cast<Family>()
                .Where(f => f.Name.Contains(key))
                .ToList();



            foreach (Family instance in customSink)
            {
                ScaleFamilyInstance(doc, instance, 15);
            }

            FamilySymbol familyType = null;

            if (customSink != null && customSink.Count > 0 && customSink[0] != null)
            {

                foreach (ElementId id in customSink[0].GetFamilySymbolIds())
                {
                    familyType = doc.GetElement(id) as FamilySymbol;
                    break;
                }
            }


            string resultado = string.Join("\n", familyNames);
            var countErrors = 0;

            foreach (var item in sinks)
            {
                CreateSink(item, commandData, familyType, ref countErrors);
            }
        }

        public void CreatingChair(List<Prediction> chairs, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Family));

            List<string> familyNames = new List<string>();


            string key = "Chair";
            var customChair = collector
                .Cast<Family>()
                .Where(f => f.Name.Contains(key))
                .ToList();



            foreach (Family instance in customChair)
            {
                ScaleFamilyInstance(doc, instance, 15);
            }

            FamilySymbol familyType = null;
            if (customChair != null && customChair.Count > 0 && customChair[0] != null)
            {

                foreach (ElementId id in customChair[0].GetFamilySymbolIds())
                {
                    familyType = doc.GetElement(id) as FamilySymbol;
                    break;
                }
            }

            string resultado = string.Join("\n", familyNames);
            var countErrors = 0;

            foreach (var item in chairs)
            {
                CreateChair(item, commandData, familyType, ref countErrors);
            }
        }

        public void CreatingBed(List<Prediction> beds, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Family));

            List<string> familyNames = new List<string>();


            string key = "Bed";
            var customBed = collector
                .Cast<Family>()
                .Where(f => f.Name.Contains(key))
                .ToList();



            foreach (Family instance in customBed)
            {
                ScaleFamilyInstance(doc, instance, 15);
            }

            FamilySymbol familyType = null;
            if (customBed != null && customBed.Count > 0 && customBed[0] != null)
            {

                foreach (ElementId id in customBed[0].GetFamilySymbolIds())
                {
                    familyType = doc.GetElement(id) as FamilySymbol;
                    break;
                }
            }

            string resultado = string.Join("\n", familyNames);
            var countErrors = 0;

            foreach (var item in beds)
            {
                CreateBed(item, commandData, familyType, ref countErrors);
            }
        }

        private void ScaleFamilyInstance(Document doc, Family instance, double scaleFactor)
        {
            try
            {

                using (Transaction trans = new Transaction(doc, "Escalar objeto"))
                {
                    trans.Start();

                    foreach (Parameter param in instance.Parameters)
                    {
                        if (param.StorageType == StorageType.Double && param.IsReadOnly == false)
                        {
                            double originalValue = param.AsDouble();
                            param.Set(originalValue * scaleFactor);
                        }
                    }


                    trans.Commit();
                }
            } catch (Exception ex) {
            
            }
        }

        public void CreatingSsofa(List<Prediction> sofas, ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Info", "O documento está em modo somente leitura.");
            }

            Level li = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(q => q.Elevation).First();


            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(Family));

            List<string> familyNames = new List<string>();


            string key = "Sofa";
            var customSofas = collector
                .Cast<Family>()
                .Where(f => f.Name.Contains(key))
                .ToList();



            foreach (Family instance in customSofas)
            {
                ScaleFamilyInstance(doc, instance, 3); 
            }

            FamilySymbol familyType = null;

            if (customSofas != null && customSofas.Count > 0 && customSofas[0] != null)
            {

                foreach (ElementId id in customSofas[0].GetFamilySymbolIds())
                {
                    familyType = doc.GetElement(id) as FamilySymbol;
                    break;
                }

            }

            string resultado = string.Join("\n", familyNames);

            var countErrors = 0;

            foreach (var item in sofas)
            {
                CreateSsofa(item, commandData, familyType, ref countErrors);
            }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elementSet)
        {

            var user = "";

            using (Login thisLogin = new Login())
            {

                thisLogin.ShowDialog();

                if (thisLogin.DialogResult == System.Windows.Forms.DialogResult.Cancel)
                {
                    return Result.Cancelled;
                }


                user = thisLogin.getUserId();

            }


            RequestData(commandData, user);

            RequestDataFurniture(commandData, user);


            return Result.Succeeded;
        }

    }
}