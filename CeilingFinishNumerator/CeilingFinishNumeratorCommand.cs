using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CeilingFinishNumerator
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class CeilingFinishNumeratorCommand : IExternalCommand
    {
        CeilingFinishNumeratorProgressBarWPF ceilingFinishNumeratorProgressBarWPF;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                _ = GetPluginStartInfo();
            }
            catch { }

            Document doc = commandData.Application.ActiveUIDocument.Document;

            Guid arRoomBookNumberGUID = new Guid("22868552-0e64-49b2-b8d9-9a2534bf0e14");
            Guid arRoomBookNameGUID = new Guid("b59a22a9-7890-45bd-9f93-a186341eef58");

            string autor = commandData.Application.Application.Username;
            List<string> uniqueOwners = new List<string>();

            // Фильтруем потолки с учетом условия
            List<Ceiling> ceilingForOwnersList = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Ceiling))
                .Cast<Ceiling>()
                .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                         || c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                .ToList();

            uniqueOwners.AddRange(GetUniqueOwners(doc, ceilingForOwnersList.Cast<Element>().ToList()));

            // Удаляем дубликаты и исключаем текущего автора
            uniqueOwners = uniqueOwners.Distinct().ToList();
            uniqueOwners.Remove(autor);

            if (uniqueOwners.Count != 0)
            {
                string owners = string.Join(Environment.NewLine, uniqueOwners.Select((str, index) => $"{index + 1}. {str}"));
                TaskDialog.Show("Revit", $"Перед началом работы попросите коллег освободить элементы, за которые они отвечают:{Environment.NewLine}{owners}");
                return Result.Cancelled;
            }

            Room firstRoom = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .Where(r => r.GetType() == typeof(Room))
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .OrderBy(r => (doc.GetElement(r.LevelId) as Level).Elevation)
                .FirstOrDefault();


            if (firstRoom == null)
            {
                TaskDialog.Show("Revit", "В проекте отсутствуют помещения. Нумерация потолка невозможна.");
                return Result.Cancelled;
            }

            // Собираем список всех параметров-строк у первого помещения (для выбора «по секциям» и т.д.)
            List<Parameter> stringParameters = firstRoom.Parameters
                .Cast<Parameter>()
                .Where(p => p.StorageType == StorageType.String)
                .OrderBy(p => p.Definition.Name, new AlphanumComparatorFastString())
                .ToList();

            List<Level> levelList = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            CeilingFinishNumeratorWPF ceilingFinishNumeratorWPF = new CeilingFinishNumeratorWPF(stringParameters, levelList);
            ceilingFinishNumeratorWPF.ShowDialog();
            if (ceilingFinishNumeratorWPF.DialogResult != true)
            {
                return Result.Cancelled;
            }
            string ceilingFinishNumberingSelectedName = ceilingFinishNumeratorWPF.CeilingFinishNumberingSelectedName;
            bool fillRoomBookParameters = ceilingFinishNumeratorWPF.FillRoomBookParameters;
            bool separatedBySections = ceilingFinishNumeratorWPF.SeparatedBySections;
            bool processSelectedLevel = ceilingFinishNumeratorWPF.ProcessSelectedLevel;
            Parameter selectedParameter = ceilingFinishNumeratorWPF.SelectedParameter;
            Level selectedLevel = ceilingFinishNumeratorWPF.SelectedLevel;

            if (ceilingFinishNumberingSelectedName == "rbt_EndToEndThroughoutTheProject")
            {
                if (separatedBySections)
                {
                    List<Room> allRoomList = new FilteredElementCollector(doc)
                       .OfClass(typeof(SpatialElement))
                       .WhereElementIsNotElementType()
                       .Where(r => r.GetType() == typeof(Room))
                       .Cast<Room>()
                       .Where(r => r.Area > 0)
                       .OrderBy(r => (doc.GetElement(r.LevelId) as Level).Elevation)
                       .ToList();

                    var sections = allRoomList
                        .OrderBy(r => r.get_Parameter(selectedParameter.Definition)?.AsString() ?? "", new AlphanumComparatorFastString())
                        .GroupBy(room => room.get_Parameter(selectedParameter.Definition)?.AsString() ?? "")
                        .ToDictionary(group => group.Key, group => group.ToList());

                    using (Transaction t = new Transaction(doc))
                    {
                        t.Start("Нумерация потолка");

                        List<Ceiling> allCeilings = new FilteredElementCollector(doc)
                            .OfClass(typeof(Ceiling))
                            .Cast<Ceiling>()
                            .Where(c =>
                            {
                                Parameter p = doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL);
                                return p != null && (p.AsString() == "Потолок" || p.AsString() == "Потолки");
                            })
                            .ToList();

                        // Проверка наличия параметров
                        if (allCeilings.First().LookupParameter("АР_НомераПомещенийПоТипуПотолка") == null)
                        {
                            TaskDialog.Show("Revit",
                                "У потолка отсутствует параметр экземпляра \"АР_НомераПомещенийПоТипуПотолка\"");
                            return Result.Cancelled;
                        }
                        if (allCeilings.First().LookupParameter("АР_ИменаПомещенийПоТипуПотолка") == null)
                        {
                            TaskDialog.Show("Revit",
                                "У потолка отсутствует параметр экземпляра \"АР_ИменаПомещенийПоТипуПотолка\"");
                            return Result.Cancelled;
                        }

                        if (fillRoomBookParameters)
                        {
                            if (allCeilings.First().get_Parameter(arRoomBookNumberGUID) == null)
                            {
                                TaskDialog.Show("Revit", "У потолка отсутствует параметр экземпляра \"АР_RoomBook_Номер\"");
                                return Result.Cancelled;
                            }

                            if (allCeilings.First().get_Parameter(arRoomBookNameGUID) == null)
                            {
                                TaskDialog.Show("Revit", "У потолка отсутствует параметр экземпляра \"АР_RoomBook_Имя\"");
                                return Result.Cancelled;
                            }
                        }

                        // Очищаем все потолки
                        foreach (Ceiling ceiling in allCeilings)
                        {
                            ceiling.LookupParameter("АР_НомераПомещенийПоТипуПотолка").Set("");
                            ceiling.LookupParameter("АР_ИменаПомещенийПоТипуПотолка").Set("");

                            if (fillRoomBookParameters)
                            {
                                ceiling.get_Parameter(arRoomBookNumberGUID).Set("");
                                ceiling.get_Parameter(arRoomBookNameGUID).Set("");
                            }
                        }

                        //Типы потолков
                        List<CeilingType> ceilingTypesList = new FilteredElementCollector(doc)
                            .OfClass(typeof(CeilingType))
                            .WhereElementIsElementType()
                            .Where(c => c.Category.Id.IntegerValue.Equals((int)BuiltInCategory.OST_Ceilings))
                            .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                            .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                            || c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                            .Cast<CeilingType>()
                            .OrderBy(с => с.Name, new AlphanumComparatorFastString())
                            .ToList();

                        Thread newWindowThread = new Thread(new ThreadStart(ThreadStartingPoint));
                        newWindowThread.SetApartmentState(ApartmentState.STA);
                        newWindowThread.IsBackground = true;
                        newWindowThread.Start();
                        int step = 0;
                        Thread.Sleep(100);
                        ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Minimum = 0);
                        ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Maximum = ceilingTypesList.Count);

                        foreach (var section in sections)
                        {
                            List<Room> roomList = section.Value;

                            foreach (CeilingType ceilingType in ceilingTypesList)
                            {
                                step++;
                                ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Value = step);
                                ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.label_ItemName.Content = ceilingType.Name);

                                List<Ceiling> ceilingList = new FilteredElementCollector(doc)
                                   .OfClass(typeof(Ceiling))
                                   .WhereElementIsNotElementType()
                                   .Cast<Ceiling>()
                                   .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                                   .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                                   || doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                                   .Where(c => c.GetTypeId() == ceilingType.Id)
                                   .ToList();
                                if (ceilingList.Count == 0) continue;

                                List<Ceiling> intersectingCeilings = new List<Ceiling>();

                                List<string> roomNumbersList = new List<string>();
                                List<string> roomNamesList = new List<string>();
                                foreach (Ceiling ceiling in ceilingList)
                                {
                                    Solid ceilingSolid = null;
                                    GeometryElement geomFloorElement = ceiling.get_Geometry(new Options());
                                    foreach (GeometryObject geomObj in geomFloorElement)
                                    {
                                        ceilingSolid = geomObj as Solid;
                                        if (ceilingSolid != null) break;
                                    }
                                    if (ceilingSolid != null)
                                    {
                                        ceilingSolid = SolidUtils.CreateTransformed(ceilingSolid, Transform.CreateTranslation(new XYZ(0, 0, -500 / 304.8)));
                                    }

                                    foreach (Room room in roomList)
                                    {
                                        Solid roomSolid = null;
                                        GeometryElement geomRoomElement = room.get_Geometry(new Options());
                                        foreach (GeometryObject geomObj in geomRoomElement)
                                        {
                                            roomSolid = geomObj as Solid;
                                            if (roomSolid != null) break;
                                        }
                                        if (roomSolid != null)
                                        {
                                            Solid intersection = null;
                                            try
                                            {
                                                intersection = BooleanOperationsUtils.ExecuteBooleanOperation(ceilingSolid, roomSolid, BooleanOperationsType.Intersect);
                                            }
                                            catch
                                            {
                                                XYZ pointForIntersect = null;
                                                FaceArray ceilingFaceArray = ceilingSolid.Faces;
                                                foreach (object planarFace in ceilingFaceArray)
                                                {
                                                    if (planarFace is PlanarFace && (planarFace as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
                                                    {
                                                        List<CurveLoop> curveLoopList = (planarFace as PlanarFace).GetEdgesAsCurveLoops().ToList();
                                                        if (curveLoopList.Count != 0)
                                                        {
                                                            CurveLoop curveLoop = curveLoopList.First();
                                                            if (curveLoop != null)
                                                            {
                                                                Curve c = curveLoop.First();
                                                                pointForIntersect = c.GetEndPoint(0);
                                                            }
                                                        }
                                                    }
                                                }
                                                if (pointForIntersect == null) continue;
                                                Curve curve = Line.CreateBound(pointForIntersect, pointForIntersect - (500 / 304.8) * XYZ.BasisZ) as Curve;
                                                SolidCurveIntersection curveIntersection = roomSolid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                                                if (curveIntersection.SegmentCount > 0)
                                                {
                                                    if (intersectingCeilings.FirstOrDefault(f => f.Id == ceiling.Id) == null)
                                                    {
                                                        intersectingCeilings.Add(ceiling);
                                                    }

                                                    if (fillRoomBookParameters)
                                                    {
                                                        if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                        }
                                                        if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        }
                                                    }

                                                    if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                    {
                                                        roomNumbersList.Add(room.Number);
                                                        roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        continue;
                                                    }
                                                }
                                            }
                                            if (intersection != null && intersection.SurfaceArea != 0)
                                            {
                                                if (intersectingCeilings.FirstOrDefault(f => f.Id == ceiling.Id) == null)
                                                {
                                                    intersectingCeilings.Add(ceiling);
                                                }

                                                if (fillRoomBookParameters)
                                                {
                                                    if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                    {
                                                        ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                    }
                                                    if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                    {
                                                        ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                    }
                                                }

                                                if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                {
                                                    roomNumbersList.Add(room.Number);
                                                    roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                }
                                            }
                                            else
                                            {
                                                XYZ pointForIntersect = null;
                                                FaceArray ceilingFaceArray = ceilingSolid.Faces;
                                                foreach (object planarFace in ceilingFaceArray)
                                                {
                                                    if (planarFace is PlanarFace && (planarFace as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
                                                    {
                                                        List<CurveLoop> curveLoopList = (planarFace as PlanarFace).GetEdgesAsCurveLoops().ToList();
                                                        if (curveLoopList.Count != 0)
                                                        {
                                                            CurveLoop curveLoop = curveLoopList.First();
                                                            if (curveLoop != null)
                                                            {
                                                                Curve c = curveLoop.First();
                                                                pointForIntersect = c.GetEndPoint(0);
                                                            }
                                                        }
                                                    }
                                                }
                                                if (pointForIntersect == null) continue;
                                                Curve curve = Line.CreateBound(pointForIntersect, pointForIntersect - (500 / 304.8) * XYZ.BasisZ) as Curve;
                                                SolidCurveIntersection curveIntersection = roomSolid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                                                if (curveIntersection.SegmentCount > 0)
                                                {
                                                    if (intersectingCeilings.FirstOrDefault(f => f.Id == ceiling.Id) == null)
                                                    {
                                                        intersectingCeilings.Add(ceiling);
                                                    }

                                                    if (fillRoomBookParameters)
                                                    {
                                                        if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                        }
                                                        if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        }
                                                    }

                                                    if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                    {
                                                        roomNumbersList.Add(room.Number);
                                                        roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        continue;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                roomNumbersList.Sort(new AlphanumComparatorFastString());
                                roomNamesList = roomNamesList.Distinct().ToList();
                                roomNamesList.Sort(new AlphanumComparatorFastString());

                                string roomNumbersByCeilingType = null;
                                string roomNamesByCeilingType = null;
                                foreach (string roomNumber in roomNumbersList)
                                {
                                    if (roomNumbersByCeilingType == null)
                                    {
                                        roomNumbersByCeilingType += roomNumber;
                                    }
                                    else
                                    {
                                        roomNumbersByCeilingType += (", " + roomNumber);
                                    }
                                }

                                foreach (string roomName in roomNamesList)
                                {
                                    if (roomNamesByCeilingType == null)
                                    {
                                        roomNamesByCeilingType += roomName;
                                    }
                                    else
                                    {
                                        roomNamesByCeilingType += (", " + roomName);
                                    }
                                }

                                foreach (Ceiling cealing in intersectingCeilings)
                                {
                                    cealing.LookupParameter("АР_НомераПомещенийПоТипуПотолка").Set(roomNumbersByCeilingType);
                                }

                                foreach (Ceiling cealing in intersectingCeilings)
                                {
                                    cealing.LookupParameter("АР_ИменаПомещенийПоТипуПотолка").Set(roomNamesByCeilingType);
                                }

                                foreach (Ceiling cealing in intersectingCeilings)
                                {
                                    if (cealing.get_Parameter(selectedParameter.Definition) != null && !cealing.get_Parameter(selectedParameter.Definition).IsReadOnly)
                                    {
                                        cealing.get_Parameter(selectedParameter.Definition).Set(section.Key);
                                    }
                                }
                            }
                        }

                        ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.Close());
                        t.Commit();
                    }
                }
                else
                {
                    List<Room> roomList = new FilteredElementCollector(doc)
                        .OfClass(typeof(SpatialElement))
                        .WhereElementIsNotElementType()
                        .Where(r => r.GetType() == typeof(Room))
                        .Cast<Room>()
                        .Where(r => r.Area > 0)
                        .OrderBy(r => (doc.GetElement(r.LevelId) as Level).Elevation)
                        .ToList();

                    using (Transaction t = new Transaction(doc))
                    {
                        t.Start("Нумерация потолка");
                        //Типы потолков
                        List<CeilingType> ceilingTypesList = new FilteredElementCollector(doc)
                            .OfClass(typeof(CeilingType))
                            .WhereElementIsElementType()
                            .Where(c => c.Category.Id.IntegerValue.Equals((int)BuiltInCategory.OST_Ceilings))
                            .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                            .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                            || c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                            .Cast<CeilingType>()
                            .OrderBy(с => с.Name, new AlphanumComparatorFastString())
                            .ToList();

                        Thread newWindowThread = new Thread(new ThreadStart(ThreadStartingPoint));
                        newWindowThread.SetApartmentState(ApartmentState.STA);
                        newWindowThread.IsBackground = true;
                        newWindowThread.Start();
                        int step = 0;
                        Thread.Sleep(100);
                        ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Minimum = 0);
                        ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Maximum = ceilingTypesList.Count);

                        foreach (CeilingType ceilingType in ceilingTypesList)
                        {
                            step++;
                            ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Value = step);
                            ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.label_ItemName.Content = ceilingType.Name);

                            List<Ceiling> ceilingList = new FilteredElementCollector(doc)
                               .OfClass(typeof(Ceiling))
                               .WhereElementIsNotElementType()
                               .Cast<Ceiling>()
                               .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                               .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                               || doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                               .Where(c => c.GetTypeId() == ceilingType.Id)
                               .ToList();
                            if (ceilingList.Count == 0) continue;


                            // Проверка наличия параметров
                            if (ceilingList.First().LookupParameter("АР_НомераПомещенийПоТипуПотолка") == null)
                            {
                                TaskDialog.Show("Revit",
                                    "У потолка отсутствует параметр экземпляра \"АР_НомераПомещенийПоТипуПотолка\"");
                                return Result.Cancelled;
                            }
                            if (ceilingList.First().LookupParameter("АР_ИменаПомещенийПоТипуПотолка") == null)
                            {
                                TaskDialog.Show("Revit",
                                    "У потолка отсутствует параметр экземпляра \"АР_ИменаПомещенийПоТипуПотолка\"");
                                return Result.Cancelled;
                            }

                            //Очистка параметра "АР_RoomBook_Номер" и "АР_RoomBook_Имя"
                            if (fillRoomBookParameters)
                            {
                                if (ceilingList.First().get_Parameter(arRoomBookNumberGUID) == null)
                                {
                                    TaskDialog.Show("Revit", "У потолка отсутствует параметр \"АР_RoomBook_Номер\"");
                                    ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.Close());
                                    return Result.Cancelled;
                                }
                                if (ceilingList.First().get_Parameter(arRoomBookNameGUID) == null)
                                {
                                    TaskDialog.Show("Revit", "У потолка отсутствует параметр \"АР_RoomBook_Имя\"");
                                    ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.Close());
                                    return Result.Cancelled;
                                }
                            }

                            foreach (Ceiling ceiling in ceilingList)
                            {
                                ceiling.LookupParameter("АР_НомераПомещенийПоТипуПотолка").Set("");
                                ceiling.LookupParameter("АР_ИменаПомещенийПоТипуПотолка").Set("");

                                if (fillRoomBookParameters)
                                {
                                    ceiling.get_Parameter(arRoomBookNumberGUID).Set("");
                                    ceiling.get_Parameter(arRoomBookNameGUID).Set("");
                                }
                            }

                            List<string> roomNumbersList = new List<string>();
                            List<string> roomNamesList = new List<string>();
                            foreach (Ceiling ceiling in ceilingList)
                            {
                                Solid ceilingSolid = null;
                                GeometryElement geomFloorElement = ceiling.get_Geometry(new Options());
                                foreach (GeometryObject geomObj in geomFloorElement)
                                {
                                    ceilingSolid = geomObj as Solid;
                                    if (ceilingSolid != null) break;
                                }
                                if (ceilingSolid != null)
                                {
                                    ceilingSolid = SolidUtils.CreateTransformed(ceilingSolid, Transform.CreateTranslation(new XYZ(0, 0, -500 / 304.8)));
                                }

                                foreach (Room room in roomList)
                                {
                                    Solid roomSolid = null;
                                    GeometryElement geomRoomElement = room.get_Geometry(new Options());
                                    foreach (GeometryObject geomObj in geomRoomElement)
                                    {
                                        roomSolid = geomObj as Solid;
                                        if (roomSolid != null) break;
                                    }
                                    if (roomSolid != null)
                                    {
                                        Solid intersection = null;
                                        try
                                        {
                                            intersection = BooleanOperationsUtils.ExecuteBooleanOperation(ceilingSolid, roomSolid, BooleanOperationsType.Intersect);
                                        }
                                        catch
                                        {
                                            XYZ pointForIntersect = null;
                                            FaceArray ceilingFaceArray = ceilingSolid.Faces;
                                            foreach (object planarFace in ceilingFaceArray)
                                            {
                                                if (planarFace is PlanarFace && (planarFace as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
                                                {
                                                    List<CurveLoop> curveLoopList = (planarFace as PlanarFace).GetEdgesAsCurveLoops().ToList();
                                                    if (curveLoopList.Count != 0)
                                                    {
                                                        CurveLoop curveLoop = curveLoopList.First();
                                                        if (curveLoop != null)
                                                        {
                                                            Curve c = curveLoop.First();
                                                            pointForIntersect = c.GetEndPoint(0);
                                                        }
                                                    }
                                                }
                                            }
                                            if (pointForIntersect == null) continue;
                                            Curve curve = Line.CreateBound(pointForIntersect, pointForIntersect - (500 / 304.8) * XYZ.BasisZ) as Curve;
                                            SolidCurveIntersection curveIntersection = roomSolid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                                            if (curveIntersection.SegmentCount > 0)
                                            {
                                                if (fillRoomBookParameters)
                                                {
                                                    if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                    {
                                                        ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                    }
                                                    if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                    {
                                                        ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                    }
                                                }

                                                if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                {
                                                    roomNumbersList.Add(room.Number);
                                                    roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                    continue;
                                                }
                                            }
                                        }
                                        if (intersection != null && intersection.SurfaceArea != 0)
                                        {
                                            if (fillRoomBookParameters)
                                            {
                                                if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                {
                                                    ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                }
                                                if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                {
                                                    ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                }
                                            }

                                            if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                            {
                                                roomNumbersList.Add(room.Number);
                                                roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                            }
                                        }
                                        else
                                        {
                                            XYZ pointForIntersect = null;
                                            FaceArray ceilingFaceArray = ceilingSolid.Faces;
                                            foreach (object planarFace in ceilingFaceArray)
                                            {
                                                if (planarFace is PlanarFace && (planarFace as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
                                                {
                                                    List<CurveLoop> curveLoopList = (planarFace as PlanarFace).GetEdgesAsCurveLoops().ToList();
                                                    if (curveLoopList.Count != 0)
                                                    {
                                                        CurveLoop curveLoop = curveLoopList.First();
                                                        if (curveLoop != null)
                                                        {
                                                            Curve c = curveLoop.First();
                                                            pointForIntersect = c.GetEndPoint(0);
                                                        }
                                                    }
                                                }
                                            }
                                            if (pointForIntersect == null) continue;
                                            Curve curve = Line.CreateBound(pointForIntersect, pointForIntersect - (500 / 304.8) * XYZ.BasisZ) as Curve;
                                            SolidCurveIntersection curveIntersection = roomSolid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                                            if (curveIntersection.SegmentCount > 0)
                                            {
                                                if (fillRoomBookParameters)
                                                {
                                                    if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                    {
                                                        ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                    }
                                                    if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                    {
                                                        ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                    }
                                                }

                                                if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                {
                                                    roomNumbersList.Add(room.Number);
                                                    roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                    continue;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            roomNumbersList.Sort(new AlphanumComparatorFastString());
                            roomNamesList = roomNamesList.Distinct().ToList();
                            roomNamesList.Sort(new AlphanumComparatorFastString());

                            string roomNumbersByCeilingType = null;
                            string roomNamesByCeilingType = null;
                            foreach (string roomNumber in roomNumbersList)
                            {
                                if (roomNumbersByCeilingType == null)
                                {
                                    roomNumbersByCeilingType += roomNumber;
                                }
                                else
                                {
                                    roomNumbersByCeilingType += (", " + roomNumber);
                                }
                            }

                            foreach (string roomName in roomNamesList)
                            {
                                if (roomNamesByCeilingType == null)
                                {
                                    roomNamesByCeilingType += roomName;
                                }
                                else
                                {
                                    roomNamesByCeilingType += (", " + roomName);
                                }
                            }

                            foreach (Ceiling ceiling in ceilingList)
                            {
                                ceiling.LookupParameter("АР_НомераПомещенийПоТипуПотолка").Set(roomNumbersByCeilingType);
                            }

                            foreach (Ceiling ceiling in ceilingList)
                            {
                                ceiling.LookupParameter("АР_ИменаПомещенийПоТипуПотолка").Set(roomNamesByCeilingType);
                            }
                        }
                        ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.Close());
                        t.Commit();
                    }
                }
            }
            else if (ceilingFinishNumberingSelectedName == "rbt_SeparatedByLevels")
            {
                if (separatedBySections)
                {
                    List<Room> allRoomList = new List<Room>();
                    if (processSelectedLevel)
                    {
                        allRoomList = new FilteredElementCollector(doc)
                           .OfClass(typeof(SpatialElement))
                           .WhereElementIsNotElementType()
                           .Where(r => r.GetType() == typeof(Room))
                           .Cast<Room>()
                           .Where(r => r.Area > 0)
                           .Where(r => r.LevelId == selectedLevel.Id)
                           .ToList();
                    }
                    else
                    {
                        allRoomList = new FilteredElementCollector(doc)
                           .OfClass(typeof(SpatialElement))
                           .WhereElementIsNotElementType()
                           .Where(r => r.GetType() == typeof(Room))
                           .Cast<Room>()
                           .Where(r => r.Area > 0)
                           .OrderBy(r => (doc.GetElement(r.LevelId) as Level).Elevation)
                           .ToList();
                    }


                    var sections = allRoomList
                        .OrderBy(r => r.get_Parameter(selectedParameter.Definition)?.AsString() ?? "", new AlphanumComparatorFastString())
                        .GroupBy(room => room.get_Parameter(selectedParameter.Definition)?.AsString() ?? "")
                        .ToDictionary(group => group.Key, group => group.ToList());


                    using (Transaction t = new Transaction(doc))
                    {
                        t.Start("Нумерация потолка");

                        List<Ceiling> ceilingListForClean = new List<Ceiling>();
                        if (processSelectedLevel)
                        {
                            ceilingListForClean = new FilteredElementCollector(doc)
                               .OfClass(typeof(Ceiling))
                               .WhereElementIsNotElementType()
                               .Cast<Ceiling>()
                               .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                               .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                               || doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                               .Where(f => f.LevelId == selectedLevel.Id)
                               .ToList();
                        }
                        else
                        {
                            ceilingListForClean = new FilteredElementCollector(doc)
                               .OfClass(typeof(Ceiling))
                               .WhereElementIsNotElementType()
                               .Cast<Ceiling>()
                               .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                               .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                               || doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                               .ToList();
                        }

                        //Очистка параметра "АР_НомераПомещенийПоТипуПотолка" и "АР_ИменаПомещенийПоТипуПотолка"
                        if (ceilingListForClean.First().LookupParameter("АР_НомераПомещенийПоТипуПотолка") == null)
                        {
                            TaskDialog.Show("Revit", "У потолка отсутствует параметр экземпляра \"АР_НомераПомещенийПоТипуПотолка\"");
                            return Result.Cancelled;
                        }

                        if (ceilingListForClean.First().LookupParameter("АР_ИменаПомещенийПоТипуПотолка") == null)
                        {
                            TaskDialog.Show("Revit", "У потолка отсутствует параметр экземпляра \"АР_ИменаПомещенийПоТипуПотолка\"");
                            return Result.Cancelled;
                        }

                        if (fillRoomBookParameters)
                        {
                            if (ceilingListForClean.First().get_Parameter(arRoomBookNumberGUID) == null)
                            {
                                TaskDialog.Show("Revit", "У потолка отсутствует параметр экземпляра \"АР_RoomBook_Номер\"");
                                return Result.Cancelled;
                            }

                            if (ceilingListForClean.First().get_Parameter(arRoomBookNameGUID) == null)
                            {
                                TaskDialog.Show("Revit", "У потолка отсутствует параметр экземпляра \"АР_RoomBook_Имя\"");
                                return Result.Cancelled;
                            }
                        }

                        foreach (Ceiling ceiling in ceilingListForClean)
                        {
                            ceiling.LookupParameter("АР_НомераПомещенийПоТипуПотолка").Set("");
                            ceiling.LookupParameter("АР_ИменаПомещенийПоТипуПотолка").Set("");

                            if (fillRoomBookParameters)
                            {
                                ceiling.get_Parameter(arRoomBookNumberGUID).Set("");
                                ceiling.get_Parameter(arRoomBookNameGUID).Set("");
                            }
                        }

                        Thread newWindowThread = new Thread(new ThreadStart(ThreadStartingPoint));
                        newWindowThread.SetApartmentState(ApartmentState.STA);
                        newWindowThread.IsBackground = true;
                        newWindowThread.Start();
                        int step = 0;
                        Thread.Sleep(100);

                        ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Minimum = 0);
                        ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Maximum = sections.Count);

                        foreach (var section in sections)
                        {
                            step++;
                            ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Value = step);
                            ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.label_ItemName.Content = section.Key);

                            List<Room> roomList = section.Value;

                            if (processSelectedLevel)
                            {
                                levelList = new List<Level>();
                                levelList.Add(selectedLevel);
                            }

                            foreach (Level lv in levelList)
                            {
                                //Типы потолков
                                List<CeilingType> ceilingTypesList = new FilteredElementCollector(doc)
                                    .OfClass(typeof(CeilingType))
                                    .WhereElementIsElementType()
                                    .Where(c => c.Category.Id.IntegerValue.Equals((int)BuiltInCategory.OST_Ceilings))
                                    .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                                    .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                                    || c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                                    .Cast<CeilingType>()
                                    .OrderBy(с => с.Name, new AlphanumComparatorFastString())
                                    .ToList();

                                foreach (CeilingType ceilingType in ceilingTypesList)
                                {
                                    List<Ceiling> ceilingList = new FilteredElementCollector(doc)
                                       .OfClass(typeof(Ceiling))
                                       .WhereElementIsNotElementType()
                                       .Cast<Ceiling>()
                                       .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                                       .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                                       || doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                                       .Where(c => c.GetTypeId() == ceilingType.Id)
                                       .Where(f => f.LevelId == lv.Id)
                                       .ToList();
                                    if (ceilingList.Count == 0) continue;

                                    List<Ceiling> intersectingCeilings = new List<Ceiling>();

                                    List<string> roomNumbersList = new List<string>();
                                    List<string> roomNamesList = new List<string>();
                                    foreach (Ceiling ceiling in ceilingList)
                                    {
                                        Solid ceilingSolid = null;
                                        GeometryElement geomFloorElement = ceiling.get_Geometry(new Options());
                                        foreach (GeometryObject geomObj in geomFloorElement)
                                        {
                                            ceilingSolid = geomObj as Solid;
                                            if (ceilingSolid != null) break;
                                        }
                                        if (ceilingSolid != null)
                                        {
                                            ceilingSolid = SolidUtils.CreateTransformed(ceilingSolid, Transform.CreateTranslation(new XYZ(0, 0, -500 / 304.8)));
                                        }

                                        foreach (Room room in roomList)
                                        {
                                            Solid roomSolid = null;
                                            GeometryElement geomRoomElement = room.get_Geometry(new Options());
                                            foreach (GeometryObject geomObj in geomRoomElement)
                                            {
                                                roomSolid = geomObj as Solid;
                                                if (roomSolid != null) break;
                                            }
                                            if (roomSolid != null)
                                            {
                                                Solid intersection = null;
                                                try
                                                {
                                                    intersection = BooleanOperationsUtils.ExecuteBooleanOperation(ceilingSolid, roomSolid, BooleanOperationsType.Intersect);
                                                }
                                                catch
                                                {
                                                    XYZ pointForIntersect = null;
                                                    FaceArray ceilingFaceArray = ceilingSolid.Faces;
                                                    foreach (object planarFace in ceilingFaceArray)
                                                    {
                                                        if (planarFace is PlanarFace && (planarFace as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
                                                        {
                                                            List<CurveLoop> curveLoopList = (planarFace as PlanarFace).GetEdgesAsCurveLoops().ToList();
                                                            if (curveLoopList.Count != 0)
                                                            {
                                                                CurveLoop curveLoop = curveLoopList.First();
                                                                if (curveLoop != null)
                                                                {
                                                                    Curve c = curveLoop.First();
                                                                    pointForIntersect = c.GetEndPoint(0);
                                                                }
                                                            }
                                                        }
                                                    }
                                                    if (pointForIntersect == null) continue;
                                                    Curve curve = Line.CreateBound(pointForIntersect, pointForIntersect - (500 / 304.8) * XYZ.BasisZ) as Curve;
                                                    SolidCurveIntersection curveIntersection = roomSolid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                                                    if (curveIntersection.SegmentCount > 0)
                                                    {
                                                        if (intersectingCeilings.FirstOrDefault(f => f.Id == ceiling.Id) == null)
                                                        {
                                                            intersectingCeilings.Add(ceiling);
                                                        }

                                                        if (fillRoomBookParameters)
                                                        {
                                                            if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                            {
                                                                ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                            }
                                                            if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                            {
                                                                ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                            }
                                                        }

                                                        if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                        {
                                                            roomNumbersList.Add(room.Number);
                                                            roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                            continue;
                                                        }
                                                    }
                                                }
                                                if (intersection != null && intersection.SurfaceArea != 0)
                                                {
                                                    if (intersectingCeilings.FirstOrDefault(f => f.Id == ceiling.Id) == null)
                                                    {
                                                        intersectingCeilings.Add(ceiling);
                                                    }

                                                    if (fillRoomBookParameters)
                                                    {
                                                        if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                        }
                                                        if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        }
                                                    }

                                                    if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                    {
                                                        roomNumbersList.Add(room.Number);
                                                        roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                    }
                                                }
                                                else
                                                {
                                                    XYZ pointForIntersect = null;
                                                    FaceArray ceilingFaceArray = ceilingSolid.Faces;
                                                    foreach (object planarFace in ceilingFaceArray)
                                                    {
                                                        if (planarFace is PlanarFace && (planarFace as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
                                                        {
                                                            List<CurveLoop> curveLoopList = (planarFace as PlanarFace).GetEdgesAsCurveLoops().ToList();
                                                            if (curveLoopList.Count != 0)
                                                            {
                                                                CurveLoop curveLoop = curveLoopList.First();
                                                                if (curveLoop != null)
                                                                {
                                                                    Curve c = curveLoop.First();
                                                                    pointForIntersect = c.GetEndPoint(0);
                                                                }
                                                            }
                                                        }
                                                    }
                                                    if (pointForIntersect == null) continue;
                                                    Curve curve = Line.CreateBound(pointForIntersect, pointForIntersect - (500 / 304.8) * XYZ.BasisZ) as Curve;
                                                    SolidCurveIntersection curveIntersection = roomSolid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                                                    if (curveIntersection.SegmentCount > 0)
                                                    {
                                                        if (intersectingCeilings.FirstOrDefault(f => f.Id == ceiling.Id) == null)
                                                        {
                                                            intersectingCeilings.Add(ceiling);
                                                        }

                                                        if (fillRoomBookParameters)
                                                        {
                                                            if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                            {
                                                                ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                            }
                                                            if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                            {
                                                                ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                            }
                                                        }

                                                        if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                        {
                                                            roomNumbersList.Add(room.Number);
                                                            roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                            continue;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    roomNumbersList.Sort(new AlphanumComparatorFastString());
                                    roomNamesList = roomNamesList.Distinct().ToList();
                                    roomNamesList.Sort(new AlphanumComparatorFastString());

                                    string roomNumbersByCeilingType = null;
                                    string roomNamesByCeilingType = null;

                                    foreach (string roomNumber in roomNumbersList)
                                    {
                                        if (roomNumbersByCeilingType == null)
                                        {
                                            roomNumbersByCeilingType += roomNumber;
                                        }
                                        else
                                        {
                                            roomNumbersByCeilingType += (", " + roomNumber);
                                        }
                                    }

                                    foreach (string roomName in roomNamesList)
                                    {
                                        if (roomNamesByCeilingType == null)
                                        {
                                            roomNamesByCeilingType += roomName;
                                        }
                                        else
                                        {
                                            roomNamesByCeilingType += (", " + roomName);
                                        }
                                    }

                                    foreach (Ceiling ceiling in intersectingCeilings)
                                    {
                                        ceiling.LookupParameter("АР_НомераПомещенийПоТипуПотолка").Set(roomNumbersByCeilingType);
                                    }

                                    foreach (Ceiling ceiling in intersectingCeilings)
                                    {
                                        ceiling.LookupParameter("АР_ИменаПомещенийПоТипуПотолка").Set(roomNamesByCeilingType);
                                    }

                                    foreach (Ceiling ceiling in intersectingCeilings)
                                    {
                                        if (ceiling.get_Parameter(selectedParameter.Definition) != null && !ceiling.get_Parameter(selectedParameter.Definition).IsReadOnly)
                                        {
                                            ceiling.get_Parameter(selectedParameter.Definition).Set(section.Key);
                                        }
                                    }
                                }
                            }
                        }

                        ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.Close());
                        t.Commit();
                    }
                }
                else
                {
                    Thread newWindowThread = new Thread(new ThreadStart(ThreadStartingPoint));
                    newWindowThread.SetApartmentState(ApartmentState.STA);
                    newWindowThread.IsBackground = true;
                    newWindowThread.Start();
                    int step = 0;
                    Thread.Sleep(100);

                    ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Minimum = 0);
                    ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Maximum = levelList.Count);

                    using (Transaction t = new Transaction(doc))
                    {
                        t.Start("Нумерация потолка");
                        if (processSelectedLevel)
                        {
                            levelList = new List<Level>();
                            levelList.Add(selectedLevel);
                        }

                        foreach (Level lv in levelList)
                        {
                            step++;
                            ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Value = step);
                            ceilingFinishNumeratorProgressBarWPF.pb_CeilingFinishNumeratorProgressBar.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.label_ItemName.Content = lv.Name);

                            List<Room> roomList = new FilteredElementCollector(doc)
                                .OfClass(typeof(SpatialElement))
                                .WhereElementIsNotElementType()
                                .Where(r => r.GetType() == typeof(Room))
                                .Cast<Room>()
                                .Where(r => r.Area > 0)
                                .Where(r => r.LevelId == lv.Id)
                                .ToList();

                            //Типы потолков
                            List<CeilingType> ceilingTypesList = new FilteredElementCollector(doc)
                                .OfClass(typeof(CeilingType))
                                .WhereElementIsElementType()
                                .Where(c => c.Category.Id.IntegerValue.Equals((int)BuiltInCategory.OST_Ceilings))
                                .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                                .Where(c => c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                                || c.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                                .Cast<CeilingType>()
                                .OrderBy(с => с.Name, new AlphanumComparatorFastString())
                                .ToList();

                            foreach (CeilingType ceilingType in ceilingTypesList)
                            {
                                List<Ceiling> ceilingList = new FilteredElementCollector(doc)
                                   .OfClass(typeof(Ceiling))
                                   .WhereElementIsNotElementType()
                                   .Cast<Ceiling>()
                                   .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL) != null)
                                   .Where(c => doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолок"
                                   || doc.GetElement(c.GetTypeId()).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Потолки")
                                   .Where(c => c.GetTypeId() == ceilingType.Id)
                                   .Where(f => f.LevelId == lv.Id)
                                   .ToList();
                                if (ceilingList.Count == 0) continue;

                                //Очистка параметра "АР_НомераПомещенийПоТипуПотолка" и "АР_ИменаПомещенийПоТипуПотолка"
                                if (ceilingList.First().LookupParameter("АР_НомераПомещенийПоТипуПотолка") == null)
                                {
                                    TaskDialog.Show("Revit", "У потолка отсутствует параметр экземпляра \"АР_НомераПомещенийПоТипуПотолка\"");
                                    ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.Close());
                                    return Result.Cancelled;
                                }

                                if (ceilingList.First().LookupParameter("АР_ИменаПомещенийПоТипуПотолка") == null)
                                {
                                    TaskDialog.Show("Revit",
                                        "У потолка отсутствует параметр экземпляра \"АР_ИменаПомещенийПоТипуПотолка\"");
                                    return Result.Cancelled;
                                }

                                foreach (Ceiling ceiling in ceilingList)
                                {
                                    ceiling.LookupParameter("АР_НомераПомещенийПоТипуПотолка").Set("");
                                    ceiling.LookupParameter("АР_ИменаПомещенийПоТипуПотолка").Set("");
                                }

                                List<string> roomNumbersList = new List<string>();
                                List<string> roomNamesList = new List<string>();
                                foreach (Ceiling ceiling in ceilingList)
                                {
                                    Solid ceilingSolid = null;
                                    GeometryElement geomFloorElement = ceiling.get_Geometry(new Options());
                                    foreach (GeometryObject geomObj in geomFloorElement)
                                    {
                                        ceilingSolid = geomObj as Solid;
                                        if (ceilingSolid != null) break;
                                    }
                                    if (ceilingSolid != null)
                                    {
                                        ceilingSolid = SolidUtils.CreateTransformed(ceilingSolid, Transform.CreateTranslation(new XYZ(0, 0, -500 / 304.8)));
                                    }

                                    foreach (Room room in roomList)
                                    {
                                        Solid roomSolid = null;
                                        GeometryElement geomRoomElement = room.get_Geometry(new Options());
                                        foreach (GeometryObject geomObj in geomRoomElement)
                                        {
                                            roomSolid = geomObj as Solid;
                                            if (roomSolid != null) break;
                                        }
                                        if (roomSolid != null)
                                        {
                                            Solid intersection = null;
                                            try
                                            {
                                                intersection = BooleanOperationsUtils.ExecuteBooleanOperation(ceilingSolid, roomSolid, BooleanOperationsType.Intersect);
                                            }
                                            catch
                                            {
                                                XYZ pointForIntersect = null;
                                                FaceArray ceilingFaceArray = ceilingSolid.Faces;
                                                foreach (object planarFace in ceilingFaceArray)
                                                {
                                                    if (planarFace is PlanarFace && (planarFace as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
                                                    {
                                                        List<CurveLoop> curveLoopList = (planarFace as PlanarFace).GetEdgesAsCurveLoops().ToList();
                                                        if (curveLoopList.Count != 0)
                                                        {
                                                            CurveLoop curveLoop = curveLoopList.First();
                                                            if (curveLoop != null)
                                                            {
                                                                Curve c = curveLoop.First();
                                                                pointForIntersect = c.GetEndPoint(0);
                                                            }
                                                        }
                                                    }
                                                }
                                                if (pointForIntersect == null) continue;
                                                Curve curve = Line.CreateBound(pointForIntersect, pointForIntersect - (500 / 304.8) * XYZ.BasisZ) as Curve;
                                                SolidCurveIntersection curveIntersection = roomSolid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                                                if (curveIntersection.SegmentCount > 0)
                                                {
                                                    if (fillRoomBookParameters)
                                                    {
                                                        if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                        }
                                                        if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        }
                                                    }

                                                    if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                    {
                                                        roomNumbersList.Add(room.Number);
                                                        roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        continue;
                                                    }
                                                }
                                            }
                                            if (intersection != null && intersection.SurfaceArea != 0)
                                            {
                                                if (fillRoomBookParameters)
                                                {
                                                    if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                    {
                                                        ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                    }
                                                    if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                    {
                                                        ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                    }
                                                }

                                                if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                {
                                                    roomNumbersList.Add(room.Number);
                                                    roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                }
                                            }
                                            else
                                            {
                                                XYZ pointForIntersect = null;
                                                FaceArray ceilingFaceArray = ceilingSolid.Faces;
                                                foreach (object planarFace in ceilingFaceArray)
                                                {
                                                    if (planarFace is PlanarFace && (planarFace as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
                                                    {
                                                        List<CurveLoop> curveLoopList = (planarFace as PlanarFace).GetEdgesAsCurveLoops().ToList();
                                                        if (curveLoopList.Count != 0)
                                                        {
                                                            CurveLoop curveLoop = curveLoopList.First();
                                                            if (curveLoop != null)
                                                            {
                                                                Curve c = curveLoop.First();
                                                                pointForIntersect = c.GetEndPoint(0);
                                                            }
                                                        }
                                                    }
                                                }
                                                if (pointForIntersect == null) continue;
                                                Curve curve = Line.CreateBound(pointForIntersect, pointForIntersect - (500 / 304.8) * XYZ.BasisZ) as Curve;
                                                SolidCurveIntersection curveIntersection = roomSolid.IntersectWithCurve(curve, new SolidCurveIntersectionOptions());
                                                if (curveIntersection.SegmentCount > 0)
                                                {
                                                    if (fillRoomBookParameters)
                                                    {
                                                        if (ceiling.get_Parameter(arRoomBookNumberGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNumberGUID).Set(room.Number);
                                                        }
                                                        if (ceiling.get_Parameter(arRoomBookNameGUID) != null)
                                                        {
                                                            ceiling.get_Parameter(arRoomBookNameGUID).Set(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        }
                                                    }

                                                    if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                                    {
                                                        roomNumbersList.Add(room.Number);
                                                        roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                                        continue;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                roomNumbersList.Sort(new AlphanumComparatorFastString());
                                roomNamesList = roomNamesList.Distinct().ToList();
                                roomNamesList.Sort(new AlphanumComparatorFastString());

                                string roomNumbersByCeilingType = null;
                                string roomNamesByCeilingType = null;

                                foreach (string roomNumber in roomNumbersList)
                                {
                                    if (roomNumbersByCeilingType == null)
                                    {
                                        roomNumbersByCeilingType += roomNumber;
                                    }
                                    else
                                    {
                                        roomNumbersByCeilingType += (", " + roomNumber);
                                    }
                                }

                                foreach (string roomName in roomNamesList)
                                {
                                    if (roomNamesByCeilingType == null)
                                    {
                                        roomNamesByCeilingType += roomName;
                                    }
                                    else
                                    {
                                        roomNamesByCeilingType += (", " + roomName);
                                    }
                                }

                                foreach (Ceiling ceiling in ceilingList)
                                {
                                    ceiling.LookupParameter("АР_НомераПомещенийПоТипуПотолка").Set(roomNumbersByCeilingType);
                                }

                                foreach (Ceiling ceiling in ceilingList)
                                {
                                    ceiling.LookupParameter("АР_ИменаПомещенийПоТипуПотолка").Set(roomNamesByCeilingType);
                                }
                            }
                        }
                        ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.Close());
                        t.Commit();
                    }
                }
            }

            return Result.Succeeded;
        }

        private void ThreadStartingPoint()
        {
            ceilingFinishNumeratorProgressBarWPF = new CeilingFinishNumeratorProgressBarWPF();
            ceilingFinishNumeratorProgressBarWPF.Show();
            System.Windows.Threading.Dispatcher.Run();
        }
        List<string> GetUniqueOwners(Document doc, List<Element> elements)
        {
            List<string> uniqueOwners = elements
                .Select(elem =>
                {
                    string owner;
                    WorksharingUtils.GetCheckoutStatus(doc, elem.Id, out owner);
                    return owner;
                })
                .Where(owner => owner != null)
                .Where(owner => !string.IsNullOrEmpty(owner))
                .Distinct()
                .ToList();

            return uniqueOwners;
        }
        private static async Task GetPluginStartInfo()
        {
            // Получаем сборку, в которой выполняется текущий код
            Assembly thisAssembly = Assembly.GetExecutingAssembly();
            string assemblyName = "CeilingFinishNumerator";
            string assemblyNameRus = "Нумератор потолка";
            string assemblyFolderPath = Path.GetDirectoryName(thisAssembly.Location);

            int lastBackslashIndex = assemblyFolderPath.LastIndexOf("\\");
            string dllPath = assemblyFolderPath.Substring(0, lastBackslashIndex + 1) + "PluginInfoCollector\\PluginInfoCollector.dll";

            Assembly assembly = Assembly.LoadFrom(dllPath);
            Type type = assembly.GetType("PluginInfoCollector.InfoCollector");

            if (type != null)
            {
                // Создание экземпляра класса
                object instance = Activator.CreateInstance(type);

                // Получение метода CollectPluginUsageAsync
                var method = type.GetMethod("CollectPluginUsageAsync");

                if (method != null)
                {
                    // Вызов асинхронного метода через reflection
                    Task task = (Task)method.Invoke(instance, new object[] { assemblyName, assemblyNameRus });
                    await task;  // Ожидание завершения асинхронного метода
                }
            }
        }
    }
}
