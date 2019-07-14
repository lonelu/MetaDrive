﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using MassSpectrometry;
using System.Diagnostics;
using System.Threading;
using System.IO;
using MetaLive;

namespace UnitTest
{
    [TestFixture]
    public static class UnitTest_Box
    {
        [SetUp]
        public static void SetUp()
        {
            //Initiate Element
            UsefulProteomicsDatabases.Loaders.LoadElements();
        }

        [Test]
        public static void boxCarRangeTest()
        {
            var Parameters = Program.AddParametersFromFile("");
            var x = Parameters.BoxCarScanSetting.BoxCarMsxInjectRanges;
            Assert.AreEqual(Parameters.BoxCarScanSetting.BoxCarScans, 2);
        }

        [Test]
        public static void dynamicBoxCarRange()
        {
            var Parameters = Program.AddParametersFromFile("");
            List<double> masses = new List<double> { 1500 };
            var test = BoxCarScan.BuildDynamicBoxString(Parameters, masses);
            Assert.AreEqual(test, "[(400.000,496.007),(506.007,746.007),(756.007,1496.007),(1506.007,1600.000)]");
        }

        [Test]
        public static void dataDependentBox()
        {
            var Parameters = Program.AddParametersFromFile("");
            List<Tuple<double, int>> Mass_Charges = new List<Tuple<double, int>> { new Tuple<double, int>(750, 2), new Tuple<double, int>(800, 2) };
            var test = DataDependentScan.BuildDataDependentBoxString(Parameters, Mass_Charges);
            Assert.AreEqual(test, "[(374.757,377.257),(399.757,402.257)]");
        }

        [Test]
        public static void test_generateMsxInjectRanges()
        {
            List<double> mz = new List<double>();
            List<double> intensity = new List<double>();
            string filePath=Path.Combine(TestContext.CurrentContext.TestDirectory, @"Data\smooth200.csv");
            using (StreamReader streamReader = new StreamReader(filePath))
            {
                int lineCount = 0;
                while (streamReader.Peek() >= 0)
                {
                    string line = streamReader.ReadLine();

                    lineCount++;
                    if (lineCount ==1)
                    {
                        continue;
                    }

                    var split = line.Split(',');
                    mz.Add(double.Parse(split[0]));
                    intensity.Add(double.Parse(split[1]));
                }
            }

            //mz = (500, 1600)
            BoxCarScanSetting boxCarScanSetting = new BoxCarScanSetting();
            boxCarScanSetting.BoxCarScans = 2;
            boxCarScanSetting.BoxCarBoxes = 12;
            boxCarScanSetting.BoxCarMzRangeLowBound = 500;
            boxCarScanSetting.BoxCarMzRangeHighBound = 1600;
            var tuples = boxCarScanSetting.SelectRanges(mz.ToArray(), intensity.ToArray());
            var ranges = boxCarScanSetting.CalculateMsxInjectRanges(tuples);
            var boxRanges = boxCarScanSetting.GenerateMsxInjectRanges(ranges);
            Assert.That(boxRanges[0] == "[(500.0,542.6),(572.0,596.1),(617.4,637.0),(655.5,673.2),(690.3,707.2),(723.8,740.3),(757.0,773.8),(791.3,809.9),(830.2,852.2),(876.8,905.8),(940.4,984.6),(1045.3,1146.1)]");

            //mz = (550, 1500)
            BoxCarScanSetting boxCarScanSetting2 = new BoxCarScanSetting();
            boxCarScanSetting2.BoxCarScans = 2;
            boxCarScanSetting2.BoxCarBoxes = 12;
            boxCarScanSetting2.BoxCarMzRangeLowBound = 550;
            boxCarScanSetting2.BoxCarMzRangeHighBound = 1550;
            var tuples2 = boxCarScanSetting2.SelectRanges(mz.ToArray(), intensity.ToArray());
            var ranges2 = boxCarScanSetting2.CalculateMsxInjectRanges(tuples2);
            var boxRanges2 = boxCarScanSetting2.GenerateMsxInjectRanges(ranges2);
            Assert.That(boxRanges2[0] == "[(550.0,572.3),(595.3,615.8),(634.6,652.4),(669.4,685.9),(702.1,718.0),(733.8,749.6),(765.6,781.9),(799.0,817.4),(837.4,859.0),(883.5,912.2),(946.5,990.4),(1050.2,1149.1)]");

        }
    }
}