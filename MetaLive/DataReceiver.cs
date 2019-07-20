﻿#region legal notice
// Copyright(c) 2016 - 2018 Thermo Fisher Scientific - LSMS
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
#endregion legal notice
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Thermo.Interfaces.ExactiveAccess_V1;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using IMsScan = Thermo.Interfaces.InstrumentAccess_V2.MsScanContainer.IMsScan;

using Thermo.Interfaces.InstrumentAccess_V1.Control.Scans;

using MassSpectrometry;
using MzLibUtil;
using Chemistry;

namespace MetaLive
{
    /// <summary>
    /// Show incoming data packets and signals of acquisition start, acquisition stop and each scan.
    /// </summary>
    class DataReceiver
    {
        IScans m_scans = null;
        public IExactiveInstrumentAccess InstrumentAccess { get; set; }
        public IMsScanContainer ScanContainer { get; set; }

        bool isTakeOver = false;
        bool dynamicExclude = true;
        bool glycoInclude = true;
        bool placeUserDefinedScan = true;
        int BoxCarScanNum = 0;
        bool TimeIsOver = false;

        static object lockerExclude = new object();
        static object lockerScan = new object();
        static object lockerGlyco = new object();

        internal DataReceiver(Parameters parameters)
        {
            Parameters = parameters;
            DeconvolutionParameter = new DeconvolutionParameter();
            UserDefinedScans = new Queue<UserDefinedScan>();
            DynamicExclusionList = new DynamicExclusionList();
            IsotopesForGlycoFeature = new IsotopesForGlycoFeature();

            switch (Parameters.GeneralSetting.MethodType)
            {
                case MethodTypes.Shutgun:
                    AddScanIntoQueueAction = AddScanIntoQueue_BottomUp;
                    Console.WriteLine("AddScanIntoQueueAction = BottomUp.");
                    break;
                case MethodTypes.StaticBoxCar:
                    AddScanIntoQueueAction = AddScanIntoQueue_StaticBox;
                    Console.WriteLine("AddScanIntoQueueAction = StaticBox.");
                    break;
                case MethodTypes.DynamicBoxCar:
                    AddScanIntoQueueAction = AddScanIntoQueue_DynamicBoxNoMS2;
                    Console.WriteLine("AddScanIntoQueueAction = DynamicBox.");
                    break;
                case MethodTypes.GlycoFeature:
                    AddScanIntoQueueAction = AddScanIntoQueue_Glyco;
                    Console.WriteLine("AddScanIntoQueueAction = Glyco.");
                    break;
                default:
                    break;
            }
        }

        Parameters Parameters { get; set; }
        DeconvolutionParameter DeconvolutionParameter { get; set; }
        Queue<UserDefinedScan> UserDefinedScans { get; set; }
        DynamicExclusionList DynamicExclusionList { get; set; }
        IsotopesForGlycoFeature IsotopesForGlycoFeature { get; set; } 

        public Action<IMsScan> AddScanIntoQueueAction { get; set; }

        internal void DetectStartSignal()
        {
            ScanContainer.MsScanArrived += Orbitrap_MsScanArrived_TakeOver;

            while(!isTakeOver)
            {
                Thread.Sleep(100);
                Console.WriteLine("Connected. Listening...");
            }
            Console.WriteLine("Detect Start Signal!");

            ScanContainer.MsScanArrived -= Orbitrap_MsScanArrived_TakeOver;
        }

        private void Orbitrap_MsScanArrived_TakeOver(object sender, MsScanEventArgs e)
        {
            using (IMsScan scan = (IMsScan)e.GetScan())
            {
                TakeOverInstrumentMessage(scan);
            }
        }

        private void TakeOverInstrumentMessage(IMsScan scan)
        {
            object massRanges;
            ThermoFisher.Foundation.IO.Range[] x = new ThermoFisher.Foundation.IO.Range[] { };
            try
            {
                if (scan.CommonInformation.TryGetRawValue("MassRanges", out massRanges))
                {
                    x = (ThermoFisher.Foundation.IO.Range[])massRanges;
                    Console.WriteLine("{0}, {1}", x.First().Low, x.First().High);

                    if (x.First().Low == 374.0 && x.First().High == 1751.0)
                    {
                        Console.WriteLine("Instrument take over Scan by IAPI is dectected.");                      
                        isTakeOver = true;

                        Console.WriteLine("Instrument take over duration time: {0}", Parameters.GeneralSetting.TotalTimeInMinute);

                        FullScan.PlaceFullScan(m_scans, Parameters);

                        Console.WriteLine("Place the first Full scan after Instrument take over.");

                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("TakeOver Execption!");
            }
        }

        internal void DoJob()
		{
            Thread childThreadCheckTime = new Thread(CheckTime);
            childThreadCheckTime.IsBackground = true;
            childThreadCheckTime.Start();
            Console.WriteLine("Start Thread for checking time!");

            Thread childThreadExclusionList = new Thread(DynamicExclusionListDeqeue);
            childThreadExclusionList.IsBackground = true;
            childThreadExclusionList.Start();
            Console.WriteLine("Start Thread for exclusion list!");


            Thread childThreadPlaceScan = new Thread(PlaceScan);
            childThreadPlaceScan.IsBackground = true;
            childThreadPlaceScan.Start();
            Console.WriteLine("Start Thread for Place Scan!");

            if (Parameters.GeneralSetting.MethodType == MethodTypes.GlycoFeature)
            {
                Thread childThreadGlycoInclusionList = new Thread(IsotopesForGlycoFeatureDeque);
                childThreadGlycoInclusionList.IsBackground = true;
                childThreadGlycoInclusionList.Start();
                Console.WriteLine("Start Thread for glyco Inclusion list!");
            }


            using (IExactiveInstrumentAccess instrument = Connection.GetFirstInstrument())
			{
                using (m_scans = instrument.Control.GetScans(false))
                {                    
                    IMsScanContainer orbitrap = instrument.GetMsScanContainer(0);
                    Console.WriteLine("Waiting for scans on detector " + orbitrap.DetectorClass + "...");

                    orbitrap.AcquisitionStreamOpening += Orbitrap_AcquisitionStreamOpening;
                    orbitrap.AcquisitionStreamClosing += Orbitrap_AcquisitionStreamClosing;
                    orbitrap.MsScanArrived += Orbitrap_MsScanArrived;

                    Thread.CurrentThread.Join(Parameters.GeneralSetting.TotalTimeInMinute * 60000);

                    orbitrap.MsScanArrived -= Orbitrap_MsScanArrived;
                    orbitrap.AcquisitionStreamClosing -= Orbitrap_AcquisitionStreamClosing;
                    orbitrap.AcquisitionStreamOpening -= Orbitrap_AcquisitionStreamOpening;
                }
            }
        }

		private void Orbitrap_MsScanArrived(object sender, MsScanEventArgs e)
		{
			// If examining code takes longer, in particular for some scans, it is wise
			// to use a processing queue in order to get the system as responsive as possible.

			using (IMsScan scan = (IMsScan) e.GetScan())	// caution! You must dispose this, or you block shared memory!
			{
                Console.WriteLine("==================================================");
                Console.WriteLine("\n{0:HH:mm:ss,fff} scan with {1} centroids arrived", DateTime.Now, scan.CentroidCount);

                //TO THINK: If the coming scan is MS2 scan, start the timing of the scan precursor into exclusion list. Currently, start when add the scan precursor.

                if (!IsMS1Scan(scan))
                {
                    Console.WriteLine("MS2 Scan arrived.");
                }
                else
                {
                    Console.WriteLine("MS1 Scan arrived.");
                    if (!TimeIsOver)
                    {
                        AddScanIntoQueueAction(scan);
                    }
                }

            }
        }

        private void Orbitrap_AcquisitionStreamOpening(object sender, MsAcquisitionOpeningEventArgs e)
        {
            Console.WriteLine("\n{0:HH:mm:ss,fff} {1}", DateTime.Now, "Acquisition stream opens (start of method)");
        }

        private void Orbitrap_AcquisitionStreamClosing(object sender, EventArgs e)
		{
            dynamicExclude = false;
            glycoInclude = false;
            placeUserDefinedScan = false;

            Console.WriteLine("\n{0:HH:mm:ss,fff} {1}", DateTime.Now, "Acquisition stream closed (end of method)");            
        }	   

        private void DynamicExclusionListDeqeue()
        {
            try
            {
                //TO DO: should I use spining or blocking
                while (dynamicExclude)
                {
                    Thread.Sleep(300);

                    DateTime dateTime = DateTime.Now;

                    Console.WriteLine("Check the dynamic exclusionList.");

                    lock (lockerExclude)
                    {
                        bool toDeque = true;

                        while (toDeque && DynamicExclusionList.exclusionList.Count >0)
                        {
                            if (dateTime.Subtract(DynamicExclusionList.exclusionList.Peek().Item3).TotalMilliseconds < Parameters.MS1IonSelecting.ExclusionDuration * 1000)
                            {
                                Console.WriteLine("The dynamic exclusionList is OK. Now: {0:HH:mm:ss,fff}, Peek: {1:HH:mm:ss,fff}.", dateTime, DynamicExclusionList.exclusionList.Peek().Item3);
                                toDeque = false;
                            }
                            else
                            {

                                DynamicExclusionList.exclusionList.Dequeue();
                                Console.WriteLine("{0:HH:mm:ss,fff} ExclusionList Dequeue: {1}", dateTime, DynamicExclusionList.exclusionList.Count);

                            }
                        }

                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("DynamicExclusionListDeqeue Exception!");
            }
        }

        private void IsotopesForGlycoFeatureDeque()
        {
            try
            {
                //TO DO: should I use spining or blocking
                while (glycoInclude)
                {
                    Thread.Sleep(300);

                    DateTime dateTime = DateTime.Now;

                    lock (lockerGlyco)
                    {
                        bool toDeque = true;

                        while (toDeque && IsotopesForGlycoFeature.isotopeList.Count > 0)
                        {
                            if (dateTime.Subtract(IsotopesForGlycoFeature.isotopeList.Peek().Item2).TotalMilliseconds < Parameters.MS1IonSelecting.ExclusionDuration * 1000)
                            {
                                Console.WriteLine("The glyco isotopeList is OK. Now: {0:HH:mm:ss,fff}, Peek: {1:HH:mm:ss,fff}.", dateTime, IsotopesForGlycoFeature.isotopeList.Peek().Item2);
                                toDeque = false;
                            }
                            else
                            {

                                IsotopesForGlycoFeature.isotopeList.Dequeue();
                                Console.WriteLine("{0:HH:mm:ss,fff} Glyco isotopeList Dequeue: {1}", dateTime, IsotopesForGlycoFeature.isotopeList.Count);

                            }
                        }

                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Glyco isotopeList Exception!");
            }
        }

        private void PlaceScan()
        {
            try
            {
                //TO DO: should I use spining or blocking
                while (placeUserDefinedScan)
                {
                    Thread.Sleep(10); //TO DO: How to control the Thread

                    //Console.WriteLine("Check the UserDefinedScans.");

                    lock (lockerScan)
                    {
                        if (UserDefinedScans.Count > 0)
                        {
                            var x = UserDefinedScans.Dequeue();
                            {
                                switch (x.UserDefinedScanType)
                                {
                                    case UserDefinedScanType.FullScan:
                                        FullScan.PlaceFullScan(m_scans, Parameters);
                                        break;
                                    case UserDefinedScanType.DataDependentScan:
                                        DataDependentScan.PlaceMS2Scan(m_scans, Parameters, x.Mz);
                                        break;
                                    case UserDefinedScanType.BoxCarScan:
                                        if (Parameters.GeneralSetting.MethodType == MethodTypes.StaticBoxCar)
                                        {
                                            BoxCarScan.PlaceBoxCarScan(m_scans, Parameters);
                                        }
                                        else
                                        {
                                            BoxCarScan.PlaceBoxCarScan(m_scans, Parameters, x.dynamicBox);
                                        }
                                        break;
                                    default:
                                        break;
                                }

                            }
                        } 
                       
                    }
                }
            }

            catch (Exception)
            {
                Console.WriteLine("PlaceScan Exception!");
            }
        }

        private void CheckTime()
        {
            Thread.CurrentThread.Join(Parameters.GeneralSetting.TotalTimeInMinute * 60000);
            TimeIsOver = true;
        }

        private void AddScanIntoQueue_BottomUp(IMsScan scan)
        {
            try
            {   
                //Is MS1 Scan
                if (scan.HasCentroidInformation && IsMS1Scan(scan))
                {
                    string scanNumber;
                    scan.CommonInformation.TryGetValue("ScanNumber", out scanNumber);
                    Console.WriteLine("MS1 Scan arrived. Is BoxCar Scan: {0}. Deconvolute.", IsBoxCarScan(scan));

                    DeconvoluteMS1ScanAddMS2Scan_TopN(scan);

                    lock (lockerScan)
                    {
                        UserDefinedScans.Enqueue(new UserDefinedScan(UserDefinedScanType.FullScan));
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("AddScanIntoQueue_BottomUp Exception!");
            }
        }

        //In StaticBox, the MS1 scan contains a lot of features. There is no need to extract features from BoxCar Scans.
        private void AddScanIntoQueue_StaticBox(IMsScan scan)
        {
            try
            {
                //Is MS1 Scan
                if (scan.HasCentroidInformation && IsMS1Scan(scan))
                {
                    bool isBoxCarScan = IsBoxCarScan(scan);

                    string scanNumber;
                    scan.CommonInformation.TryGetValue("ScanNumber", out scanNumber);
                    Console.WriteLine("In StaticBox method, MS1 Scan arrived. Is BoxCar Scan: {0}.", isBoxCarScan);

                    if (!isBoxCarScan && Parameters.MS2ScanSetting.DoMS2)
                    {
                        DeconvoluteMS1ScanAddMS2Scan_TopN(scan);
                    }

                    if (isBoxCarScan)
                    {
                        BoxCarScanNum--;
                    }

                    if (BoxCarScanNum == 0)
                    {
                        lock (lockerScan)
                        {
                            UserDefinedScans.Enqueue(new UserDefinedScan(UserDefinedScanType.FullScan));
                            UserDefinedScans.Enqueue(new UserDefinedScan(UserDefinedScanType.BoxCarScan));
                        }
                        BoxCarScanNum = Parameters.BoxCarScanSetting.BoxCarScans;
                    }

                }
            }
            catch (Exception)
            {
                Console.WriteLine("AddScanIntoQueue_StaticBoxMS2FromFullScan Exception!");
            }
        }

        private void AddScanIntoQueue_DynamicBoxNoMS2(IMsScan scan)
        {
            try
            {
                //Is MS1 Scan
                if (scan.HasCentroidInformation && IsMS1Scan(scan))
                {
                    bool isBoxCarScan = IsBoxCarScan(scan);

                    string scanNumber;
                    scan.CommonInformation.TryGetValue("ScanNumber", out scanNumber);
                    Console.WriteLine("MS1 Scan arrived. Is BoxCar Scan: {0}.", isBoxCarScan);

                    if (!isBoxCarScan)
                    {
                        //TO THINK: The time with DeconvoluateDynamicBoxRange need to be considered.
                        var dynamicRanges = DeconvoluateDynamicBoxRange(scan);
                        lock (lockerScan)
                        {
                            if (dynamicRanges.Count!=0)
                            {
                                var newDefinedScan = new UserDefinedScan(UserDefinedScanType.BoxCarScan);
                                newDefinedScan.dynamicBox = dynamicRanges;
                                UserDefinedScans.Enqueue(newDefinedScan);
                            }
                            else
                            {
                                //If no dynamic box scan placed, place static box scan.
                                UserDefinedScans.Enqueue(new UserDefinedScan(UserDefinedScanType.BoxCarScan));
                            }
                            UserDefinedScans.Enqueue(new UserDefinedScan(UserDefinedScanType.FullScan));
                        }
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("AddScanIntoQueue_DynamicBoxNoMS2 Exception!");
            }
        }

        private void AddScanIntoQueue_Glyco(IMsScan scan)
        {
            try
            {
                //Is MS1 Scan
                if (scan.HasCentroidInformation && IsMS1Scan(scan))
                {
                    string scanNumber;
                    scan.CommonInformation.TryGetValue("ScanNumber", out scanNumber);
                    Console.WriteLine("MS1 Scan arrived. Is BoxCar Scan: {0}. Deconvolute.", IsBoxCarScan(scan));

                    DeconvoluteAndFindGlycoFeatures(scan);

                    lock (lockerScan)
                    {
                        UserDefinedScans.Enqueue(new UserDefinedScan(UserDefinedScanType.FullScan));
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("AddScanIntoQueue_BottomUp Exception!");
            }
        }

        private bool IsMS1Scan(IMsScan scan)
        {
            string value;
            if (scan.CommonInformation.TryGetValue("MSOrder", out value))
            {
                if (value == "MS")
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsBoxCarScan(IMsScan scan)
        {
            //TO DO: better way to check if is boxcar scan.
            string value;
            string valueHigh;
            if (scan.CommonInformation.TryGetValue("LowMass", out value) && scan.CommonInformation.TryGetValue("HighMass", out valueHigh))
            {
                Console.WriteLine("IsBoxCarScan: " + value + "," + Parameters.BoxCarScanSetting.BoxCarMzRangeLowBound.ToString());

                string massRangeCount;
                if (scan.CommonInformation.TryGetValue("MassRangeCount", out massRangeCount))
                {
                    Console.WriteLine("BoxCar Scan Boxes: {0}.", int.Parse(massRangeCount));
                }

                if (value == Parameters.BoxCarScanSetting.BoxCarMzRangeLowBound.ToString() && valueHigh == Parameters.BoxCarScanSetting.BoxCarMzRangeHighBound.ToString())
                {
                    return true;
                }
            }
            return false;

            //string massRangeCount;

            //if (scan.CommonInformation.TryGetValue("MassRangeCount", out massRangeCount))
            //{
            //    Console.WriteLine("BoxCar Scan Boxes: {0}.", int.Parse(massRangeCount));

            //    if (int.Parse(massRangeCount) == Parameters.BoxCarScanSetting.BoxCarBoxes)
            //    {
            //        return true;
            //    }
            //}

            //return false;
        }

        private void DeconvoluteMS1ScanAddMS2Scan(IMsScan scan)
        {
            Console.WriteLine("\n{0:HH:mm:ss,fff} Deconvolute Start", DateTime.Now);

            var spectrum = new MzSpectrumBU(scan.Centroids.Select(p => p.Mz).ToArray(), scan.Centroids.Select(p => p.Intensity).ToArray(), true);

            Console.WriteLine("\n{0:HH:mm:ss,fff} Deconvolute Creat spectrum", DateTime.Now);

            var IsotopicEnvelopes = spectrum.Deconvolute(spectrum.Range, DeconvolutionParameter);

            Console.WriteLine("\n{0:HH:mm:ss,fff} Deconvolute Finished", DateTime.Now, IsotopicEnvelopes.Count());

            IsotopicEnvelopes = IsotopicEnvelopes.OrderByDescending(p => p.totalIntensity).ToArray();

            Console.WriteLine("\n{0:HH:mm:ss,fff} Deconvolute order by intensity", DateTime.Now, IsotopicEnvelopes.Count());

            if (IsotopicEnvelopes.Count() > 0)
            {
                int topN = 0;
                foreach (var iso in IsotopicEnvelopes)
                {
                    if (topN >= Parameters.MS1IonSelecting.TopN)
                    {
                        break;
                    }
                    lock (lockerExclude)
                    {
                        if (DynamicExclusionList.isNotInExclusionList(iso.monoisotopicMass, 1.25))
                        {
                            var dataTime = DateTime.Now;
                            DynamicExclusionList.exclusionList.Enqueue(new Tuple<double, int, DateTime>(iso.monoisotopicMass, iso.charge, dataTime));
                            Console.WriteLine("ExclusionList Enqueue: {0}", DynamicExclusionList.exclusionList.Count);

                            var theScan = new UserDefinedScan(UserDefinedScanType.DataDependentScan);

                            theScan.Mz = iso.monoisotopicMass.ToMz(iso.charge);
                            lock (lockerScan)
                            {
                                UserDefinedScans.Enqueue(theScan);
                                Console.WriteLine("dataDependentScans increased.");
                            }
                            topN++;
                        }
                    }
                }
            }

        }

        //This deconvolution only deconvolute TopN peaks to generate MS2 scans. Deconvolute one peak, add one MS2 scan. 
        //There is no need to wait to deconvolute all peaks and then add all MS2 scans. 
        //In theory, this should reduce the time for deconvolute all peaks.
        private void DeconvoluteMS1ScanAddMS2Scan_TopN(IMsScan scan)
        {
            Console.WriteLine("\n{0:HH:mm:ss,fff} Deconvolute Start", DateTime.Now);

            var spectrum = new MzSpectrumBU(scan.Centroids.Select(p => p.Mz).ToArray(), scan.Centroids.Select(p => p.Intensity).ToArray(), false);

            HashSet<double> seenPeaks = new HashSet<double>();
            int topN = 0;
            foreach (var peakIndex in spectrum.ExtractIndicesByY())
            {
                if (topN >= Parameters.MS1IonSelecting.TopN)
                {
                    break;
                }

                if (seenPeaks.Contains(spectrum.XArray[peakIndex]))
                {
                    continue;
                }
                var iso = spectrum.DeconvolutePeak(peakIndex, DeconvolutionParameter);
                if (iso == null)
                {
                    continue;
                }
                foreach (var seenPeak in iso.peaks.Select(b => b.mz))
                {
                    seenPeaks.Add(seenPeak);
                }

                lock (lockerExclude)
                {
                    if (DynamicExclusionList.isNotInExclusionList(iso.monoisotopicMass, 1.25))
                    {
                        var dataTime = DateTime.Now;
                        DynamicExclusionList.exclusionList.Enqueue(new Tuple<double, int, DateTime>(iso.monoisotopicMass, iso.charge, dataTime));
                        Console.WriteLine("ExclusionList Enqueue: {0}", DynamicExclusionList.exclusionList.Count);

                        var theScan = new UserDefinedScan(UserDefinedScanType.DataDependentScan);

                        //TO DO: get the best Mz.
                        theScan.Mz = iso.monoisotopicMass.ToMz(iso.charge);
                        lock (lockerScan)
                        {
                            UserDefinedScans.Enqueue(theScan);
                            Console.WriteLine("dataDependentScans increased.");
                        }
                        topN++;
                    }
                }
            }
        }

        private List<double> DeconvoluateDynamicBoxRange(IMsScan scan)
        {
            List<double> dynamicRange = new List<double>();

            //TO DO: Finish the function.


            return dynamicRange;
        }

        private void DeconvoluteAndFindGlycoFeatures(IMsScan scan)
        {
            var spectrum = new MzSpectrumBU(scan.Centroids.Select(p => p.Mz).ToArray(), scan.Centroids.Select(p => p.Intensity).ToArray(), true);

            Console.WriteLine("\n{0:HH:mm:ss,fff} Deconvolute Creat spectrum", DateTime.Now);

            var IsotopicEnvelopes = spectrum.Deconvolute(spectrum.Range, DeconvolutionParameter).OrderBy(p=>p.monoisotopicMass).ToArray();

            Console.WriteLine("\n{0:HH:mm:ss,fff} Deconvolute Finished", DateTime.Now, IsotopicEnvelopes.Count());

            NeuCodeIsotopicEnvelop[] allIsotops;

            lock (lockerGlyco)
            {
                var dataTime = DateTime.Now;
                foreach (var isotop in IsotopicEnvelopes)
                {
                    IsotopesForGlycoFeature.isotopeList.Enqueue(new Tuple<NeuCodeIsotopicEnvelop, DateTime>(isotop, dataTime));
                }

                allIsotops = IsotopesForGlycoFeature.isotopeList.Select(p => p.Item1).OrderBy(p => p.monoisotopicMass).ToArray();
            }

            var features = FeatureFinder.ExtractGlycoMS1features(allIsotops);

            Console.WriteLine("\n{0:HH:mm:ss,fff} Deconvolute Finished", DateTime.Now, features.Count());

            //TO DO: if features.Count < topN, place random 5 scans.
            int topN = 0;
            if (features.Count() > 0)
            {
                
                foreach (var iso in features)
                {
                    if (topN >= Parameters.GlycoSetting.TopN)
                    {
                        break;
                    }
                    lock (lockerExclude)
                    {
                        if (DynamicExclusionList.isNotInExclusionList(iso.Key, 1.25))
                        {
                            var dataTime = DateTime.Now;
                            DynamicExclusionList.exclusionList.Enqueue(new Tuple<double, int, DateTime>(iso.Key, iso.Value, dataTime));
                            Console.WriteLine("ExclusionList Enqueue: {0}", DynamicExclusionList.exclusionList.Count);

                            var theScan = new UserDefinedScan(UserDefinedScanType.DataDependentScan);

                            theScan.Mz = iso.Key.ToMz(iso.Value);
                            lock (lockerScan)
                            {
                                UserDefinedScans.Enqueue(theScan);
                                Console.WriteLine("dataDependentScans increased.");
                            }
                            topN++;
                        }
                    }
                }
            }

            if (topN < Parameters.MS1IonSelecting.TopN && IsotopicEnvelopes.Count() > 0)
            {
                foreach (var iso in IsotopicEnvelopes)
                {
                    if (topN >= Parameters.MS1IonSelecting.TopN)
                    {
                        break;
                    }
                    lock (lockerExclude)
                    {
                        if (DynamicExclusionList.isNotInExclusionList(iso.monoisotopicMass, 1.25))
                        {
                            var dataTime = DateTime.Now;
                            DynamicExclusionList.exclusionList.Enqueue(new Tuple<double, int, DateTime>(iso.monoisotopicMass, iso.charge, dataTime));
                            Console.WriteLine("ExclusionList Enqueue: {0}", DynamicExclusionList.exclusionList.Count);

                            var theScan = new UserDefinedScan(UserDefinedScanType.DataDependentScan);

                            theScan.Mz = iso.monoisotopicMass.ToMz(iso.charge);
                            lock (lockerScan)
                            {
                                UserDefinedScans.Enqueue(theScan);
                                Console.WriteLine("dataDependentScans increased.");
                            }
                            topN++;
                        }
                    }
                }
            }

        }

    }
}
