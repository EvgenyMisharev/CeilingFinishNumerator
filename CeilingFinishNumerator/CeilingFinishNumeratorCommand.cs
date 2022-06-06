﻿using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            Document doc = commandData.Application.ActiveUIDocument.Document;

            CeilingFinishNumeratorWPF ceilingFinishNumeratorWPF = new CeilingFinishNumeratorWPF();
            ceilingFinishNumeratorWPF.ShowDialog();
            if (ceilingFinishNumeratorWPF.DialogResult != true)
            {
                return Result.Cancelled;
            }
            string ceilingFinishNumberingSelectedName = ceilingFinishNumeratorWPF.CeilingFinishNumberingSelectedName;

            if (ceilingFinishNumberingSelectedName == "rbt_EndToEndThroughoutTheProject")
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


                        //Очистка параметра "Помещение_Список номеров"
                        if (ceilingList.First().LookupParameter("АР_НомераПомещенийПоТипуПотолка") == null)
                        {
                            TaskDialog.Show("Revit", "У потолка отсутствует параметр экземпляра \"АР_НомераПомещенийПоТипуПотолка\"");
                            ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() => ceilingFinishNumeratorProgressBarWPF.Close());
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
                                        //ПРОПИСАТЬ ЛОГИКУ СБОРА ОШИБОК!!!!
                                    }
                                    if (intersection != null && intersection.SurfaceArea != 0)
                                    {
                                        if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                        {
                                            roomNumbersList.Add(room.Number);
                                            roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                        }
                                    }
                                }
                            }
                        }
                        roomNumbersList.Sort(new AlphanumComparatorFastString());
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

            else if (ceilingFinishNumberingSelectedName == "rbt_SeparatedByLevels")
            {
                List<Level> levelList = new FilteredElementCollector(doc)
                   .OfClass(typeof(Level))
                   .WhereElementIsNotElementType()
                   .Cast<Level>()
                   .OrderBy(l => l.Elevation)
                   .ToList();
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
                                TaskDialog.Show("Revit", "У пола отсутствует параметр экземпляра \"АР_НомераПомещенийПоТипуПотолка\"");
                                ceilingFinishNumeratorProgressBarWPF.Dispatcher.Invoke(() =>ceilingFinishNumeratorProgressBarWPF.Close());
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
                                            //ПРОПИСАТЬ ЛОГИКУ СБОРА ОШИБОК!!!!
                                        }
                                        if (intersection != null && intersection.SurfaceArea != 0)
                                        {
                                            if (roomNumbersList.Find(elem => elem == room.Number) == null)
                                            {
                                                roomNumbersList.Add(room.Number);
                                                roomNamesList.Add(room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                                            }
                                        }
                                    }
                                }
                            }

                            roomNumbersList.Sort(new AlphanumComparatorFastString());
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
            return Result.Succeeded;
        }
        private void ThreadStartingPoint()
        {
            ceilingFinishNumeratorProgressBarWPF = new CeilingFinishNumeratorProgressBarWPF();
            ceilingFinishNumeratorProgressBarWPF.Show();
            System.Windows.Threading.Dispatcher.Run();
        }
    }
}