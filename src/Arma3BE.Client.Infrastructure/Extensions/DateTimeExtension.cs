﻿using Arma3BEClient.Libs.Tools;
using System;

namespace Arma3BE.Client.Infrastructure.Extensions
{
    public static class DateTimeExtensions
    {
        private static ISettingsStore _settingsStore => new SettingsStoreSource().GetSettingsStore();

        public static DateTime UtcToLocalFromSettings(this DateTime source)
        {
            var zone = _settingsStore.TimeZoneInfo;
            return TimeZoneInfo.ConvertTimeFromUtc(source, zone);
        }


        public static DateTime LocalToUtcFromSettings(this DateTime source)
        {
            var zone = _settingsStore.TimeZoneInfo;
            return TimeZoneInfo.ConvertTimeToUtc(source, zone);
        }


        public static DateTime? UtcToLocalFromSettings(this DateTime? source)
        {
            return source?.UtcToLocalFromSettings();
        }


        public static DateTime? LocalToUtcFromSettings(this DateTime? source)
        {
            return source?.LocalToUtcFromSettings();
        }
    }
}