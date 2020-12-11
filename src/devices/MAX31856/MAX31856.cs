﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Device.Spi;
using System.IO;
using UnitsNet;

namespace Iot.Device.MAX31856
{
    /// <summary>
    /// Documentation https://datasheets.maximintegrated.com/en/ds/MAX31856.pdf
    /// </summary>
    public class MAX31856 : IDisposable
    {
        private readonly byte _thermocoupleType;
        private SpiDevice _spiDevice;

        #region SpiSettings

        /// <summary>
        /// Spi Clock Frequency from the Technical Manual of the device
        /// </summary>
        public const int SpiClockFrequency = 5_000_000;

        /// <summary>
        /// Spi Clock Frequency from the Technical Manual of the device
        /// </summary>
        public const int DataFlow = 0;

        /// <summary>
        /// MAX31856 SPI Mode
        /// </summary>
        public const SpiMode SpiMode = System.Device.Spi.SpiMode.Mode1;

        #endregion

        /// <summary>
        /// Command to Get Temperature from the device
        /// </summary>
        /// <remarks>
        /// Initializes the device and then reads the data next it converts the bytes to thermocouple temperature
        /// </remarks>
        public bool TryGetTemperature(out Temperature temperature)
        {
            try
            {
                temperature = ReadTemperature(out byte[] spiOutputData);
                return true;
            }
            catch (IOException)
            {
                temperature = new Temperature();
                return false;
            }
        }

        /// <summary>
        ///  Reads the temperature from the Cold-Junction sensor
        /// </summary>
        /// <returns>
        /// Temperature, precision +- 0.7 Celsius range from -20 Celsius to +85 Celsius
        /// </returns>
        public Temperature GetColdJunctionTemperature() => Temperature.FromDegreesCelsius(ReadCJTemperature());

        /// <summary>
        /// Creates a new instance of the max31856.
        /// </summary>
        /// <param name="spiDevice">The communications channel to a device on a SPI bus</param>
        /// <param name="thermocoupleType">Thermocouple Type T,K,R</param>
        public MAX31856(SpiDevice spiDevice, ThermocoupleType thermocoupleType)
        {
            _spiDevice = spiDevice ?? throw new ArgumentNullException(nameof(spiDevice));
            _thermocoupleType = (byte)thermocoupleType;
            Initialize();
        }

        #region private and internal

        /// <summary>
        /// Standard initialization routine.
        /// </summary>
        /// /// <remarks>
        /// You can add new write lines if you want to alter the settings of the device. Settings can be found in the Technical Manual
        /// </remarks>
        private void Initialize()
        {
            Span<byte> configurationSetting0 = stackalloc byte[]
            {
                (byte)Register.WRITE_CR0,
                (byte)Register.ONESHOT_FAULT_SETTING
            };
            Span<byte> configurationSetting1 = stackalloc byte[]
            {
                (byte)Register.WRITE_CR1,
                _thermocoupleType
            };

            Write(configurationSetting0);
            Write(configurationSetting1);

        }

        /// <summary>
        /// Command to Get Temperature from the Device
        /// </summary>
        /// <remarks>
        /// Initializes the device and then reads the data for the cold junction temperature also checks for errors to throw
        /// </remarks>
        private double ReadCJTemperature()
        {
            var spiOutputData = WriteRead(Register.READ_CR0, 16);
            var coldJunctionTemperature = ConvertspiOutputDataTempColdJunction(spiOutputData);
            return coldJunctionTemperature;
        }

        /// <summary>
        /// Converts the Thermocouple Temperature Reading
        /// </summary>
        /// <remarks>
        /// Takes the spi data as an input and outputs the Thermocouple Temperature Reading
        /// </remarks>
        private Temperature ReadTemperature(out byte[] spiOutputData)
        {
            spiOutputData = WriteRead(Register.READ_CR0, 16);
            double tempRaw = (((spiOutputData[13] & 0x7F) << 16) + (spiOutputData[14] << 8) + (spiOutputData[15]));
            double temperature = tempRaw / 4096; // temperature in degrees C
            if ((spiOutputData[13] & 0x80) == 0x80) // checks if the temp is negative
            {
                temperature = -temperature;
            }

            // Throws exceptions if the device is not found or if the thermocouple is not connected
            if (spiOutputData[1] != (byte)Register.ONESHOT_FAULT_SETTING)
            {
                throw new IOException("Device not found");
            }
            else if (((spiOutputData[16] & (byte)Register.ERROR_OPEN) == (byte)Register.ERROR_OPEN))
            {
                throw new IOException("Thermocouple is not connected");
            }

            var temperatureOut = Temperature.FromDegreesCelsius(temperature); // Converts temperature to type Temperature struct and establishes it as Degrees C for its initial units

            return temperatureOut;
        }

        /// <summary>
        /// Converts Cold Junction Temperature Reading
        /// </summary>
        /// <remarks>
        /// Takes the spi data as an input and outputs the Cold Junction Temperature Reading
        /// </remarks>
        /// <param name="spiOutputData">Spidata read from the device as 16 bytes</param>
        private double ConvertspiOutputDataTempColdJunction(byte[] spiOutputData)
        {
            var tempRaw = (((spiOutputData[11] & 0x7F) << 8) + spiOutputData[12]);
            double temperatureColdJunction = tempRaw / 256; // convert decimal ouput to temperature using a constant from the Technical Manual
            if ((spiOutputData[11] & 0x80) == 0x80) // checks if the temp is negative
            {
                temperatureColdJunction = -temperatureColdJunction;
            }

            return temperatureColdJunction;
        }

        #endregion

        #region read and write operations

        /// <summary>
        /// Writes the Data to the Spi Device
        /// </summary>
        /// <remarks>
        /// Takes the data input byte and writes it to the spi device
        /// </remarks>
        /// <param name="data">Data to write to the device</param>
        private void Write(Span<byte> data) => _spiDevice.Write(data);

        /// <summary>
        /// Full Duplex Read of the Data on the Device
        /// </summary>
        /// <remarks>
        /// Writes the read address of the register and outputs a byte list of the length provided
        /// </remarks>
        /// /// <param name="register">Register location to write to which starts the device reading</param>
        /// /// <param name="readbytesize">Number of bytes being read</param>
        private byte[] WriteRead(Register register, int readbytesize)
        {
            Span<byte> readBuf = stackalloc byte[readbytesize + 1];
            Span<byte> regAddrBuf = stackalloc byte[1 + readbytesize];

            regAddrBuf[0] = (byte)(register);
            _spiDevice.TransferFullDuplex(regAddrBuf, readBuf);
            var rawData = readBuf.ToArray();
            return rawData;
        }

        #endregion

        /// <inheritdoc/>
        public void Dispose()
        {
            _spiDevice?.Dispose();
            _spiDevice = null!;
        }

    }
}
