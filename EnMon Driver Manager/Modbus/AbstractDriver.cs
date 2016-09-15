﻿using IniParser;
using IniParser.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace EnMon_Driver_Manager.Modbus
{
    /// <summary>
    ///
    /// </summary>
    public abstract class AbstractDriver
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the analog signals.
        /// </summary>
        /// <value>
        /// The analog signals.
        /// </value>
        public List<AnalogSignal> AnalogSignals { get; set; }

        /// <summary>
        /// Gets or sets the binary signals.
        /// </summary>
        /// <value>
        /// The binary signals.
        /// </value>
        public List<BinarySignal> BinarySignals { get; set; }

        /// <summary>
        /// Gets or sets the devices.
        /// </summary>
        /// <value>
        /// The devices.
        /// </value>
        public List<Device> Devices { get; set; }

        /// <summary>
        /// Gets or sets the polling time.
        /// </summary>
        /// <value>
        /// The polling time.
        /// </value>
        public int PollingTime { get; set; }

        /// <summary>
        /// Gets or sets the read time out.
        /// </summary>
        /// <value>
        /// The read time out.
        /// </value>
        public int ReadTimeOut { get; set; }

        /// <summary>
        /// Gets or sets the retry number.
        /// </summary>
        /// <value>
        /// The retry number.
        /// </value>
        public int RetryNumber { get; set; }

        #endregion Public Properties

        #region Private Properties

        public static int pollingTime;
        public static int portNumber;
        public static int readTimeOut;
        public static int retryNumber;
        private List<AnalogSignal> analogSignals;
        private List<BinarySignal> binarySignals;
        private Thread thread_ReadAnalogValues;

        private Thread thread_ReadBinaryValues;

        /// <summary>
        /// Gets or sets the database helper.
        /// </summary>
        /// <value>
        /// The database helper.
        /// </value>
        protected static DBHelper dbHelper { get; set; }

        #endregion Private Properties

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="AbstractDriver"/> class.
        /// </summary>
        public AbstractDriver() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AbstractDriver"/> class.
        /// </summary>
        /// <param name="_configFile">The configuration file.</param>
        public AbstractDriver(string _configFile)
        {
            dbHelper = new DBHelper();
            Devices = new List<Device>();

            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(_configFile);

            var _stations = data["Stations"]["Names"];

            if (_stations != null)
            {
                string[] _stationNames = _stations.Split(',');
                if (_stationNames != null)
                {
                    foreach (string _station in _stationNames)
                    {
                        List<Device> _stationDevices = dbHelper.GetStationDevices(_station.Trim());

                        if (_stationDevices.Count > 0)
                        {
                            foreach (Device d in _stationDevices)
                            {
                                d.BinarySignals = dbHelper.GetDeviceBinarySignalsInfo(d.ID);
                                d.AnalogSignals = dbHelper.GetDeviceAnalogSignalsInfo(d.ID);
                            }

                            Devices.AddRange(_stationDevices);
                        }
                        else
                        {
                            Log.Instance.Info("{0} adlı station için herhang bir device bulunamadı", _station);
                        }
                    }

                    if (Devices != null)
                    {
                        VerifyProtocolofDevices();

                        if (Devices != null)
                        {
                            InitializeDriver();
                        }
                        else
                        {
                            Log.Instance.Error("{0} Hata: İstasyon cihazları haberleşme protokolü hatası.", this.GetType().Name);
                        }
                    }
                    else
                    {
                        Log.Instance.Error("{0} Hata: İstasyonlara ait modbus cihazı bulunamadı.", this.GetType().Name);
                    }
                }
                else
                {
                    Log.Instance.Error("{0} Hata: ModbusDriverConfig.ini dosyası veri girişi hatası.", this.GetType().Name);
                }
            }
            else
            {
                Log.Instance.Error("{0} Hata: ModbusDriverConfig.ini dosyasında kayıtlı istasyon bulunamadı.", this.GetType().Name);
            }
            
        }

        #endregion Constructors

        #region Public Methods

        /// <summary>
        /// Gets the signal list for driver devices.
        /// </summary>
        public void GetSignalListForDriverDevices()
        {
            Log.Instance.Trace("GetSignalListForDriverDevices fonksiyonu cagrıldı");

            DBHelper db_connection = new DBHelper();
            foreach (Device _device in Devices)
            {
                _device.BinarySignals = db_connection.GetDeviceBinarySignalsInfo(_device.ID);
                _device.AnalogSignals = db_connection.GetDeviceAnalogSignalsInfo(_device.ID);
            }
        }

        /// <summary>
        /// Starts the communication.
        /// </summary>
        public void StartCommunication()
        {
            Log.Instance.Trace("{0}: {1} methodu çağrıldı cagrıldı", this.GetType().Name, MethodBase.GetCurrentMethod().Name);
            try
            {
                ConnectToModbusDevices();
                dbHelper.WriteValuesAtBufferToDatabase();
            }
            catch (Exception e)
            {
                Log.Instance.Fatal("{0}: Driver baglantı hatası => ", this.GetType().Name, e.Message);
            }
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>
        /// Driver'da tanımlı tüm device'ların database'de kayıtlı sinyallerinin database'deki son güncel degerlerini alır.
        /// </summary>
        private void GetLatestValuesFromDatabase()
        {
            Log.Instance.Trace("GetLastValuesFromDatabase fonksiyonu cagrıldı");

            // Yeni bir database baglantı nesnesi olusturuluyor
            DBHelper db_connection = new DBHelper();

            // Her device için donguye giriliyor
            foreach (Device device in Devices)
            {
                // Database'den currentValue bilgisi ile donen liste gecici olarak olusturulan List<BinarySignal>'e atanıyor.
                List<BinarySignal> _binarySignals = new List<BinarySignal>();
                _binarySignals = db_connection.GetDeviceBinarySignalsValue(device.ID);

                if (_binarySignals != null)
                {
                    foreach (BinarySignal signal in device.BinarySignals)
                    {
                        signal.CurrentValue = _binarySignals.First(s => s.Name == signal.Name).CurrentValue;
                        signal.TimeTag = _binarySignals.First(s => s.Name == signal.Name).TimeTag;
                    }
                }
                else
                {
                    Log.Instance.Error("{0} (ID No:{1}) device'ı için database'den son degerler okunamadı", device.Name, device.ID);
                }

                // Database'den currentValue bilgisi ile donen liste gecici olarak olusturulan List<AnalogSignal>'e atanıyor.
                List<AnalogSignal> _analogSignals = new List<AnalogSignal>();
                _analogSignals = db_connection.GetDeviceAnalogSignalsValue(device.ID);

                if (_analogSignals != null)
                {
                    foreach (AnalogSignal signal in device.AnalogSignals)
                    {
                        signal.CurrentValue = _analogSignals.First(s => s.Name == signal.Name).CurrentValue;
                    }
                }
                else
                {
                    Log.Instance.Error("{0} (ID No:{1}) device'ı için database'den son degerler okunamadı", device.Name, device.ID);
                }
            }
        }

        #endregion Private Methods

        #region Abstract Methods

        /// <summary>
        /// Device'tan istenilen sinyalin degerini okur
        /// </summary>
        /// <returns></returns>
        //abstract public void ReadBinaryValuesFromDevice();

        /// <summary>
        /// Reads the analog values from device.
        /// </summary>
        //abstract public void ReadAnalogValuesfromDevice();

        /// <summary>
        /// Writes the value to device.
        /// </summary>
        //abstract public void WriteValueToDevice();

        /// <summary>
        /// Connects this instance.
        /// </summary>
        protected abstract void ConnectToModbusDevices();

        /// <summary>
        /// Initializes the driver.
        /// </summary>
        protected abstract void InitializeDriver();

        /// <summary>
        /// Devices listesindeki tüm cihazların dogru protokol ile haberleşip haberleşmediğini kontrol eder.
        /// Protokolü yanlış seçilmiş device varsa onu driver'in devices listesinden çıkartır ve o device ile baglantı kurmaz.
        /// </summary>
        protected abstract void VerifyProtocolofDevices();
        /// <summary>
        /// Starts to read values.
        /// </summary>
        //abstract public void StartToReadValues();

        #endregion Abstract Methods
    }
}