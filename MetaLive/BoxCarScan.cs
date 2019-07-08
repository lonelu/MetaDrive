﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Thermo.Interfaces.InstrumentAccess_V1.Control.Scans;
using Chemistry;

namespace MetaLive
{
    class BoxCarScan
    {
        public static string[] StaticBoxCar_2_12_Scan = new string[2]
        { "[(400, 423.2), (441.2, 459.9), (476.3, 494.3), (510.3, 528.8), (545, 563.8), (580.8, 600.3), (618.4, 639.8), " +
                "(660.3, 684.3), (708.3, 735.4), (764.4, 799.9),(837.9, 885.4), (945, 1032)]",
        "[(422.2,442.2), (458.9,477.3), (493.3,511.3), (527.8,546), (562.8,581.8), (599.3, 619.4), (638.8, 661.3), " +
                "(683.3, 709.3), (734.4, 765.4), (798.9, 838.9), (884.4, 946), (1031, 1201)]"
        };

        public static void PlaceBoxCarScan(IScans m_scans, Parameters parameters)
        {
            if (m_scans.PossibleParameters.Length == 0)
            {
                return;
            }

            ICustomScan scan = m_scans.CreateCustomScan();
            scan.Values["FirstMass"] = parameters.BoxCarScanSetting.BoxCarMzRangeLowBound.ToString();
            scan.Values["LastMass"] = parameters.BoxCarScanSetting.BoxCarMzRangeHighBound.ToString();
            scan.Values["IsolationRangeLow"] = (parameters.BoxCarScanSetting.BoxCarMzRangeLowBound - 200).ToString();
            scan.Values["IsolationRangeHigh"] = (parameters.BoxCarScanSetting.BoxCarMzRangeHighBound + 200).ToString();

            scan.Values["MaxIT"] = parameters.BoxCarScanSetting.BoxCarMaxInjectTimeInMillisecond.ToString();
            scan.Values["Resolution"] = parameters.BoxCarScanSetting.BoxCarResolution.ToString();
            scan.Values["Polarity"] = parameters.GeneralSetting.Polarity.ToString();
            scan.Values["NCE"] = "0.0";
            scan.Values["NCE_NormCharge"] = parameters.BoxCarScanSetting.BoxCarNormCharge.ToString();
            scan.Values["NCE_SteppedEnergy"] = "0";
            scan.Values["NCE_Factors"] = "[]";

            scan.Values["SourceCID"] = parameters.GeneralSetting.SourceCID.ToString("0.00");
            scan.Values["Microscans"] = parameters.BoxCarScanSetting.BoxCarMicroScans.ToString();
            scan.Values["AGC_Target"] = parameters.BoxCarScanSetting.BoxCarAgcTarget.ToString();
            scan.Values["AGC_Mode"] = parameters.GeneralSetting.AGC_Mode.ToString();

            scan.Values["MsxInjectTargets"] = parameters.BoxCarScanSetting.BoxCarMsxInjectTargets;
            scan.Values["MsxInjectMaxITs"] = parameters.BoxCarScanSetting.BoxCarMsxInjectMaxITs;
            scan.Values["MsxInjectNCEs"] = "[]";
            scan.Values["MsxInjectDirectCEs"] = "[]";
            for (int i = 0; i < parameters.BoxCarScanSetting.BoxCarScans; i++)
            {
                scan.Values["MsxInjectRanges"] = StaticBoxCar_2_12_Scan[i];

                Console.WriteLine("{0:HH:mm:ss,fff} placing BoxCar MS1 scan", DateTime.Now);
                m_scans.SetCustomScan(scan);
            }           
        }

        public static void PlaceBoxCarScan(IScans m_scans, Parameters parameters, List<double> dynamicBox)
        {
            if (m_scans.PossibleParameters.Length == 0)
            {
                return;
            }

            ICustomScan scan = m_scans.CreateCustomScan();
            scan.Values["Resolution"] = parameters.BoxCarScanSetting.BoxCarResolution.ToString();
            scan.Values["FirstMass"] = parameters.BoxCarScanSetting.BoxCarMzRangeLowBound.ToString();
            scan.Values["LastMass"] = parameters.BoxCarScanSetting.BoxCarMzRangeHighBound.ToString();
            scan.Values["MaxIT"] = parameters.BoxCarScanSetting.BoxCarMaxInjectTimeInMillisecond.ToString();
            scan.Values["NCE_NormCharge"] = parameters.BoxCarScanSetting.BoxCarNormCharge.ToString();
            scan.Values["AGC_Target"] = parameters.BoxCarScanSetting.BoxCarAgcTarget.ToString();

            
            var dynamicBoxString = BuildDynamicBoxString(parameters, dynamicBox);
            scan.Values["MsxInjectRanges"] = dynamicBoxString;

            Console.WriteLine("{0:HH:mm:ss,fff} placing Dynamic BoxCar MS1 scan {1}", DateTime.Now, dynamicBoxString);
            m_scans.SetCustomScan(scan);

        }

        public static string BuildDynamicBoxString(Parameters parameters, List<double> dynamicBox)
        {
            string dynamicBoxRanges = "[";
            List<double> mzs = new List<double>();
            foreach (var range in dynamicBox)
            {
                for (int i = 1; i < 60; i++)
                {
                    mzs.Add(range.ToMz(i));
                }
            }

            var mzsFiltered = mzs.Where(p => p > parameters.BoxCarScanSetting.BoxCarMzRangeLowBound && p < parameters.BoxCarScanSetting.BoxCarMzRangeHighBound).OrderBy(p => p).ToList();

            dynamicBoxRanges += "(";
            dynamicBoxRanges += parameters.BoxCarScanSetting.BoxCarMzRangeLowBound.ToString("0.000");
            dynamicBoxRanges += ",";
            if (mzsFiltered[0] - 5 < parameters.BoxCarScanSetting.BoxCarMzRangeLowBound)
            {
                dynamicBoxRanges += (mzsFiltered[0]).ToString("0.000");
            }
            else
            {
                dynamicBoxRanges += (mzsFiltered[0] - 5).ToString("0.000");
            }
            dynamicBoxRanges += "),";

            for (int i = 1; i < mzsFiltered.Count; i++)
            {
                var mz = mzsFiltered[i];
                var mz_front = mzsFiltered[i - 1];
                dynamicBoxRanges += "(";
                dynamicBoxRanges += (mz_front + 5).ToString("0.000");
                dynamicBoxRanges += ",";
                dynamicBoxRanges += (mz - 5).ToString("0.000");
                dynamicBoxRanges += "),";
            }
            dynamicBoxRanges += "(";
            dynamicBoxRanges += (mzsFiltered.Last() + 5).ToString("0.000");
            dynamicBoxRanges += ",";
            dynamicBoxRanges += parameters.BoxCarScanSetting.BoxCarMzRangeHighBound.ToString("0.000");
            dynamicBoxRanges += ")";

            dynamicBoxRanges += "]";
            return dynamicBoxRanges;
        }

    }
}
