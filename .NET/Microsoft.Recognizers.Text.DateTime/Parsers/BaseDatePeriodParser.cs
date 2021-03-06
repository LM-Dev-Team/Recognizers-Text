﻿using System;
using System.Collections.Generic;
using System.Globalization;
using DateObject = System.DateTime;

using Microsoft.Recognizers.Text.Number;
using System.Text.RegularExpressions;

namespace Microsoft.Recognizers.Text.DateTime
{
    public class BaseDatePeriodParser : IDateTimeParser
    {
        public static readonly string ParserName = Constants.SYS_DATETIME_DATEPERIOD; //"DatePeriod";
        
        private static readonly Calendar Cal = DateTimeFormatInfo.InvariantInfo.Calendar;

        private readonly IDatePeriodParserConfiguration config;

        private static bool InclusiveEndPeriod = false;

        public BaseDatePeriodParser(IDatePeriodParserConfiguration configuration)
        {
            config = configuration;
        }

        public ParseResult Parse(ExtractResult result)
        {
            return this.Parse(result, DateObject.Now);
        }

        public DateTimeParseResult Parse(ExtractResult er, DateObject refDate)
        {
            var referenceDate = refDate;

            object value = null;
            
            if (er.Type.Equals(ParserName))
            {
                var innerResult = ParseMonthWithYear(er.Text, referenceDate);
                if (!innerResult.Success)
                {
                    innerResult = ParseSimpleCases(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseOneWordPeriod(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = MergeTwoTimePoints(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseYear(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseWeekOfMonth(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseWeekOfYear(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseQuarter(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseSeason(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseWhichWeek(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseWeekOfDate(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseMonthOfDate(er.Text, referenceDate);
                }

                if (!innerResult.Success)
                {
                    innerResult = ParseDecade(er.Text, referenceDate);
                }

                // Parse duration should be at the end since it will extract "the last week" from "the last week of July"
                if (!innerResult.Success)
                {
                    innerResult = ParseDuration(er.Text, referenceDate);
                }

                if (innerResult.Success)
                {
                    if (innerResult.FutureValue != null && innerResult.PastValue != null)
                    {
                        innerResult.FutureResolution = new Dictionary<string, string>
                        {
                            {
                                TimeTypeConstants.START_DATE,
                                FormatUtil.FormatDate(((Tuple<DateObject, DateObject>) innerResult.FutureValue).Item1)
                            },
                            {
                                TimeTypeConstants.END_DATE,
                                FormatUtil.FormatDate(((Tuple<DateObject, DateObject>) innerResult.FutureValue).Item2)
                            }
                        };

                        innerResult.PastResolution = new Dictionary<string, string>
                        {
                            {
                                TimeTypeConstants.START_DATE,
                                FormatUtil.FormatDate(((Tuple<DateObject, DateObject>) innerResult.PastValue).Item1)
                            },
                            {
                                TimeTypeConstants.END_DATE,
                                FormatUtil.FormatDate(((Tuple<DateObject, DateObject>) innerResult.PastValue).Item2)
                            }
                        };
                    }
                    else
                    {
                        innerResult.FutureResolution = innerResult.PastResolution = new Dictionary<string, string>();
                    }

                    value = innerResult;
                }
            }

            var ret = new DateTimeParseResult
            {
                Text = er.Text,
                Start = er.Start,
                Length = er.Length,
                Type = er.Type,
                Data = er.Data,
                Value = value,
                TimexStr = value == null ? "" : ((DateTimeResolutionResult)value).Timex,
                ResolutionStr = ""
            };

            return ret;
        }

        private DateTimeResolutionResult ParseSimpleCases(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            int year = referenceDate.Year, month = referenceDate.Month;
            int beginDay, endDay;
            var noYear = true;

            var trimedText = text.Trim();
            var match = this.config.MonthFrontBetweenRegex.Match(trimedText);
            string beginLuisStr, endLuisStr;

            if (!match.Success)
            {
                match = this.config.BetweenRegex.Match(trimedText);
            }

            if (!match.Success)
            {
                match = this.config.MonthFrontSimpleCasesRegex.Match(trimedText);
            }

            if (!match.Success)
            {
                match = this.config.SimpleCasesRegex.Match(trimedText);
            }

            if (match.Success && match.Index == 0 && match.Length == trimedText.Length)
            {
                var days = match.Groups["day"];
                beginDay = this.config.DayOfMonth[days.Captures[0].Value.ToLower()];
                endDay = this.config.DayOfMonth[days.Captures[1].Value.ToLower()];

                // parse year
                year = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(match);
                if (year != Constants.InvalidYear)
                {
                    noYear = false;
                }
                else
                {
                    year = referenceDate.Year;
                }

                var monthStr = match.Groups["month"].Value;
                if (!string.IsNullOrEmpty(monthStr))
                {
                    month = this.config.MonthOfYear[monthStr.ToLower()];
                }
                else
                {
                    monthStr = match.Groups["relmonth"].Value.Trim().ToLower();
                    var swiftMonth = this.config.GetSwiftDayOrMonth(monthStr);
                    switch (swiftMonth)
                    {
                        case 1:
                            if (month != 12)
                            {
                                month += 1;
                            }
                            else
                            {
                                month = 1;
                                year += 1;
                            }
                            break;
                        case -1:
                            if (month != 1)
                            {
                                month -= 1;
                            }
                            else
                            {
                                month = 12;
                                year -= 1;
                            }
                            break;
                        default:
                            break;
                    }

                    if (this.config.IsFuture(monthStr))
                    {
                        noYear = false;
                    }
                }
            }
            else
            {
                return ret;
            }
            
            if (noYear)
            {
                beginLuisStr = FormatUtil.LuisDate(-1, month, beginDay);
                endLuisStr = FormatUtil.LuisDate(-1, month, endDay);
            }
            else
            {
                beginLuisStr = FormatUtil.LuisDate(year, month, beginDay);
                endLuisStr = FormatUtil.LuisDate(year, month, endDay);
            }

            int futureYear = year, pastYear = year;
            var startDate = DateObject.MinValue.SafeCreateFromValue(year, month, beginDay);

            if (noYear && startDate < referenceDate)
            {
                futureYear++;
            }

            if (noYear && startDate >= referenceDate)
            {
                pastYear--;
            }

            ret.Timex = $"({beginLuisStr},{endLuisStr},P{endDay - beginDay}D)";
            ret.FutureValue = new Tuple<DateObject, DateObject>(
                DateObject.MinValue.SafeCreateFromValue(futureYear, month, beginDay),
                DateObject.MinValue.SafeCreateFromValue(futureYear, month, endDay));
            ret.PastValue = new Tuple<DateObject, DateObject>(
                DateObject.MinValue.SafeCreateFromValue(pastYear, month, beginDay),
                DateObject.MinValue.SafeCreateFromValue(pastYear, month, endDay));
            ret.Success = true;

            return ret;
        }

        private DateTimeResolutionResult ParseOneWordPeriod(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            int year = referenceDate.Year, month = referenceDate.Month;
            int futureYear = year, pastYear = year;
            var earlyPrefix = false;
            var latePrefix = false;
            var midPrefix = false;

            var trimedText = text.Trim().ToLower();
            var match = this.config.OneWordPeriodRegex.Match(trimedText);

            if (!(match.Success && match.Index == 0 && match.Length == trimedText.Length))
            {
                match = this.config.LaterEarlyPeriodRegex.Match(trimedText);
            }

            if (match.Success && match.Index == 0 && match.Length == trimedText.Length)
            {
                if (match.Groups["EarlyPrefix"].Success)
                {
                    earlyPrefix = true;
                    trimedText = match.Groups["suffix"].ToString();
                    ret.Mod = Constants.EARLY_MOD;
                }
                else if (match.Groups["LatePrefix"].Success)
                {
                    latePrefix = true;
                    trimedText = match.Groups["suffix"].ToString();
                    ret.Mod = Constants.LATE_MOD;
                }
                else if (match.Groups["MidPrefix"].Success)
                {
                    midPrefix = true;
                    trimedText = match.Groups["suffix"].ToString();
                    ret.Mod = Constants.MID_MOD;
                }

                var monthStr = match.Groups["month"].Value;
                if (this.config.IsYearToDate(trimedText))
                {
                    ret.Timex = referenceDate.Year.ToString("D4");
                    ret.FutureValue =
                        ret.PastValue =
                            new Tuple<DateObject, DateObject>(DateObject.MinValue.SafeCreateFromValue(referenceDate.Year, 1, 1), referenceDate);
                    ret.Success = true;
                    return ret;
                }

                if (this.config.IsMonthToDate(trimedText))
                {
                    ret.Timex = referenceDate.Year.ToString("D4") + "-" + referenceDate.Month.ToString("D2");
                    ret.FutureValue =
                        ret.PastValue =
                            new Tuple<DateObject, DateObject>(
                                DateObject.MinValue.SafeCreateFromValue(referenceDate.Year, referenceDate.Month, 1), referenceDate);
                    ret.Success = true;
                    return ret;
                }

                if (!string.IsNullOrEmpty(monthStr))
                {
                    var swift = this.config.GetSwiftYear(trimedText);

                    month = this.config.MonthOfYear[monthStr.ToLower()];

                    if (swift >= -1)
                    {
                        ret.Timex = (referenceDate.Year + swift).ToString("D4") + "-" + month.ToString("D2");
                        year = year + swift;
                        futureYear = pastYear = year;
                    }
                    else
                    {
                        ret.Timex = "XXXX-" + month.ToString("D2");
                        if (month < referenceDate.Month)
                        {
                            futureYear++;
                        }

                        if (month >= referenceDate.Month)
                        {
                            pastYear--;
                        }
                    }
                }
                else
                {
                    var swift = this.config.GetSwiftDayOrMonth(trimedText);

                    if (this.config.IsWeekOnly(trimedText))
                    {
                        var monday = referenceDate.This(DayOfWeek.Monday).AddDays(7 * swift);

                        ret.Timex = FormatUtil.ToIsoWeekTimex(monday);
                        var beginDate = referenceDate.This(DayOfWeek.Monday).AddDays(7 * swift);
                        var endDate = InclusiveEndPeriod
                                        ? referenceDate.This(DayOfWeek.Sunday).AddDays(7 * swift)
                                        : referenceDate.This(DayOfWeek.Sunday).AddDays(7 * swift).AddDays(1);

                        if (earlyPrefix)
                        {
                            endDate = InclusiveEndPeriod
                                        ? referenceDate.This(DayOfWeek.Wednesday).AddDays(7 * swift)
                                        : referenceDate.This(DayOfWeek.Wednesday).AddDays(7 * swift).AddDays(1);
                        }
                        else if (midPrefix)
                        {
                            beginDate = referenceDate.This(DayOfWeek.Tuesday).AddDays(7 * swift);
                            endDate = InclusiveEndPeriod
                                        ? referenceDate.This(DayOfWeek.Friday).AddDays(7 * swift)
                                        : referenceDate.This(DayOfWeek.Friday).AddDays(7 * swift).AddDays(1);
                        }
                        else if (latePrefix)
                        {
                            beginDate = referenceDate.This(DayOfWeek.Thursday).AddDays(7 * swift);
                        }

                        ret.FutureValue =
                            ret.PastValue =
                                new Tuple<DateObject, DateObject>(beginDate, endDate);

                        ret.Success = true;
                        return ret;
                    }

                    if (this.config.IsWeekend(trimedText))
                    {
                        var beginDate = referenceDate.This(DayOfWeek.Saturday).AddDays(7 * swift);
                        var endDate = referenceDate.This(DayOfWeek.Sunday).AddDays(7 * swift);

                        ret.Timex = beginDate.Year.ToString("D4") + "-W" +
                                    Cal.GetWeekOfYear(beginDate, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)
                                        .ToString("D2") + "-WE";

                        endDate = InclusiveEndPeriod ? endDate : endDate.AddDays(1);

                        ret.FutureValue =
                            ret.PastValue = new Tuple<DateObject, DateObject>(beginDate, endDate);

                        ret.Success = true;
                        return ret;
                    }

                    if (this.config.IsMonthOnly(trimedText))
                    {
                        month = referenceDate.AddMonths(swift).Month;
                        year = referenceDate.AddMonths(swift).Year;
                        ret.Timex = year.ToString("D4") + "-" + month.ToString("D2");
                        futureYear = pastYear = year;
                    }
                    else if (this.config.IsYearOnly(trimedText))
                    {
                        year = referenceDate.AddYears(swift).Year;

                        var beginDate = DateObject.MinValue.SafeCreateFromValue(year, 1, 1);
                        var endDate = InclusiveEndPeriod
                                    ? DateObject.MinValue.SafeCreateFromValue(year, 12, 31)
                                    : DateObject.MinValue.SafeCreateFromValue(year, 12, 31).AddDays(1);
                        if (earlyPrefix)
                        {
                            endDate = InclusiveEndPeriod
                                    ? DateObject.MinValue.SafeCreateFromValue(year, 6, 30)
                                    : DateObject.MinValue.SafeCreateFromValue(year, 6, 30).AddDays(1);
                        }
                        else if (midPrefix)
                        {
                            beginDate = DateObject.MinValue.SafeCreateFromValue(year, 4, 1);
                            endDate = InclusiveEndPeriod
                                    ? DateObject.MinValue.SafeCreateFromValue(year, 9, 30)
                                    : DateObject.MinValue.SafeCreateFromValue(year, 9, 30).AddDays(1);
                        }
                        else if (latePrefix)
                        {
                            beginDate = DateObject.MinValue.SafeCreateFromValue(year, 7, 1);
                        }

                        ret.Timex = year.ToString("D4");

                        ret.FutureValue =
                            ret.PastValue =
                                new Tuple<DateObject, DateObject>(beginDate, endDate);

                        ret.Success = true;
                        return ret;
                    }
                }
            }
            else
            {
                return ret;
            }

            // only "month" will come to here
            var futureStart = DateObject.MinValue.SafeCreateFromValue(futureYear, month, 1);
            var futureEnd = InclusiveEndPeriod
                ? DateObject.MinValue.SafeCreateFromValue(futureYear, month, 1).AddMonths(1).AddDays(-1)
                : DateObject.MinValue.SafeCreateFromValue(futureYear, month, 1).AddMonths(1);
            var pastStart = DateObject.MinValue.SafeCreateFromValue(pastYear, month, 1);
            var pastEnd = InclusiveEndPeriod
                ? DateObject.MinValue.SafeCreateFromValue(pastYear, month, 1).AddMonths(1).AddDays(-1)
                : DateObject.MinValue.SafeCreateFromValue(pastYear, month, 1).AddMonths(1);
            if (earlyPrefix)
            {
                futureEnd = InclusiveEndPeriod
                    ? DateObject.MinValue.SafeCreateFromValue(futureYear, month, 15)
                    : DateObject.MinValue.SafeCreateFromValue(futureYear, month, 15).AddDays(1);
                pastEnd = InclusiveEndPeriod
                    ? DateObject.MinValue.SafeCreateFromValue(pastYear, month, 15)
                    : DateObject.MinValue.SafeCreateFromValue(pastYear, month, 15).AddDays(1);
            }
            else if (midPrefix)
            {
                futureStart = DateObject.MinValue.SafeCreateFromValue(futureYear, month, 10);
                pastStart = DateObject.MinValue.SafeCreateFromValue(pastYear, month, 10);
                futureEnd = InclusiveEndPeriod
                    ? DateObject.MinValue.SafeCreateFromValue(futureYear, month, 20)
                    : DateObject.MinValue.SafeCreateFromValue(futureYear, month, 20).AddDays(1);
                pastEnd = InclusiveEndPeriod
                    ? DateObject.MinValue.SafeCreateFromValue(pastYear, month, 20)
                    : DateObject.MinValue.SafeCreateFromValue(pastYear, month, 20).AddDays(1);
            }
            else if (latePrefix)
            {
                futureStart = DateObject.MinValue.SafeCreateFromValue(futureYear, month, 16);
                pastStart = DateObject.MinValue.SafeCreateFromValue(pastYear, month, 16);
            }

            ret.FutureValue = new Tuple<DateObject, DateObject>(futureStart, futureEnd);

            ret.PastValue = new Tuple<DateObject, DateObject>(pastStart, pastEnd);

            ret.Success = true;

            return ret;
        }

        private DateTimeResolutionResult ParseMonthWithYear(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();

            var match = this.config.MonthWithYear.Match(text);
            if (!match.Success)
            {
                match = this.config.MonthNumWithYear.Match(text);
            }

            if (match.Success && match.Length == text.Length)
            {
                var monthStr = match.Groups["month"].Value.ToLower();
                var orderStr = match.Groups["order"].Value.ToLower();

                var month = this.config.MonthOfYear[monthStr.ToLower()];

                int year = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(match);
                if (year == Constants.InvalidYear)
                {
                    var swift = this.config.GetSwiftYear(orderStr);
                    if (swift < -1)
                    {
                        return ret;
                    }
                    year = referenceDate.Year + swift;
                }

                ret.FutureValue = ret.PastValue = new Tuple<DateObject, DateObject>(
                    DateObject.MinValue.SafeCreateFromValue(year, month, 1),
                    InclusiveEndPeriod
                        ? DateObject.MinValue.SafeCreateFromValue(year, month, 1).AddMonths(1).AddDays(-1)
                        : DateObject.MinValue.SafeCreateFromValue(year, month, 1).AddMonths(1));

                ret.Timex = year.ToString("D4") + "-" + month.ToString("D2");

                ret.Success = true;
            }

            return ret;
        }

        private DateTimeResolutionResult ParseYear(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            int year = Constants.InvalidYear;

            var match = this.config.YearPeriodRegex.Match(text);
            if (match.Success)
            {
                int beginYear = Constants.InvalidYear;
                int endYear = Constants.InvalidYear;

                var matches = this.config.YearRegex.Matches(text);
                if (matches.Count == 2)
                {
                    // (from|during|in|between)? 2012 (till|to|until|through|-) 2015
                    if (matches[0].Success)
                    {
                        beginYear = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(matches[0]);
                        if (!(beginYear >= this.config.MinYearNum && beginYear <= this.config.MaxYearNum))
                        {
                            beginYear = Constants.InvalidYear;
                        }
                    }

                    if (matches[1].Success)
                    {
                        endYear = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(matches[1]);
                        if (!(endYear >= this.config.MinYearNum && endYear <= this.config.MaxYearNum))
                        {
                            endYear = Constants.InvalidYear;
                        }
                    }
                }

                if (beginYear != Constants.InvalidYear && endYear != Constants.InvalidYear)
                {
                    var beginDay = DateObject.MinValue.SafeCreateFromValue(beginYear, 1, 1);

                    var endDay = InclusiveEndPeriod
                            ? DateObject.MinValue.SafeCreateFromValue(endYear, 1, 1).AddDays(-1)
                            : DateObject.MinValue.SafeCreateFromValue(endYear, 1, 1);

                    ret.Timex = $"({FormatUtil.LuisDate(beginDay)},{FormatUtil.LuisDate(endDay)},P{endYear - beginYear}Y)";
                    ret.FutureValue = ret.PastValue = new Tuple<DateObject, DateObject>(beginDay, endDay);
                    ret.Success = true;

                    return ret;
                }
            }
            else
            {
                match = this.config.YearRegex.Match(text);
                if (match.Success && match.Length == text.Trim().Length)
                {
                    year = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(match);
                    if (!(year >= this.config.MinYearNum && year <= this.config.MaxYearNum))
                    {
                        year = Constants.InvalidYear;
                    }
                }
                else
                {
                    match = this.config.YearPlusNumberRegex.Match(text);
                    if (match.Success && match.Length == text.Trim().Length)
                    {
                        year = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(match);
                    }
                }

                if (year != Constants.InvalidYear)
                {
                    var beginDay = DateObject.MinValue.SafeCreateFromValue(year, 1, 1);

                    var endDay = InclusiveEndPeriod
                            ? DateObject.MinValue.SafeCreateFromValue(year + 1, 1, 1).AddDays(-1)
                            : DateObject.MinValue.SafeCreateFromValue(year + 1, 1, 1);

                    ret.Timex = year.ToString("D4");
                    ret.FutureValue = ret.PastValue = new Tuple<DateObject, DateObject>(beginDay, endDay);
                    ret.Success = true;

                    return ret;
                }
            }

            return ret;
        }

        // parse entities that made up by two time points
        private DateTimeResolutionResult MergeTwoTimePoints(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();

            var er = this.config.DateExtractor.Extract(text, referenceDate);
            if (er.Count < 2)
            {
                er = this.config.DateExtractor.Extract(this.config.TokenBeforeDate + text, referenceDate);
                if (er.Count < 2)
                {
                    return ret;
                }
                er[0].Start -= this.config.TokenBeforeDate.Length;
                er[1].Start -= this.config.TokenBeforeDate.Length;
            }

            var match = this.config.WeekWithWeekDayRangeRegex.Match(text);
            string weekPrefix = null;
            if (match.Success)
            {
                weekPrefix = match.Groups["week"].ToString();
            }

            if (!string.IsNullOrEmpty(weekPrefix))
            {
                er[0].Text = weekPrefix + " " + er[0].Text;
                er[1].Text = weekPrefix + " " + er[1].Text;
            }

            var pr1 = this.config.DateParser.Parse(er[0], referenceDate);
            var pr2 = this.config.DateParser.Parse(er[1], referenceDate);
            if (pr1.Value == null || pr2.Value == null)
            {
                return ret;
            }

            ret.SubDateTimeEntities= new List<object> { pr1, pr2 };

            DateObject futureBegin = (DateObject)((DateTimeResolutionResult)pr1.Value).FutureValue,
                futureEnd = (DateObject)((DateTimeResolutionResult)pr2.Value).FutureValue;

            DateObject pastBegin = (DateObject)((DateTimeResolutionResult)pr1.Value).PastValue,
                pastEnd = (DateObject)((DateTimeResolutionResult)pr2.Value).PastValue;

            if (futureBegin > futureEnd)
            {
                futureBegin = pastBegin;
            }

            if (pastEnd < pastBegin)
            {
                pastEnd = futureEnd;
            }
            
            ret.Timex = $"({pr1.TimexStr},{pr2.TimexStr},P{(futureEnd - futureBegin).TotalDays}D)";
            ret.FutureValue = new Tuple<DateObject, DateObject>(futureBegin, futureEnd);
            ret.PastValue = new Tuple<DateObject, DateObject>(pastBegin, pastEnd);
            ret.Success = true;

            return ret;
        }

        private DateTimeResolutionResult ParseDuration(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            DateObject beginDate;
            var endDate = beginDate = referenceDate;
            string timex = string.Empty;
            bool restNowSunday = false;

            var ers = config.DurationExtractor.Extract(text, referenceDate);
            if (ers.Count == 1)
            {
                var pr = config.DurationParser.Parse(ers[0]);
                var beforeStr = text.Substring(0, pr.Start ?? 0).Trim().ToLowerInvariant();
                var afterStr = text.Substring((pr.Start ?? 0) + (pr.Length ?? 0)).Trim().ToLowerInvariant();
                var mod = "";

                if (pr.Value != null)
                {
                    var durationResult = (DateTimeResolutionResult) pr.Value;

                    if (string.IsNullOrEmpty(durationResult.Timex))
                    {
                        return ret;
                    }

                    var prefixMatch = config.PastRegex.Match(beforeStr);
                    if (prefixMatch.Success)
                    {
                        GetModAndDate(ref beginDate, ref endDate, referenceDate, durationResult.Timex, false, out mod);
                    }
                    else
                    {
                        var suffixMatch = config.PastRegex.Match(afterStr);
                        if (suffixMatch.Success)
                        {
                            GetModAndDate(ref beginDate, ref endDate, referenceDate, durationResult.Timex, false, out mod);
                        }
                    }

                    prefixMatch = config.FutureRegex.Match(beforeStr);
                    if (prefixMatch.Success && prefixMatch.Length == beforeStr.Length)
                    {
                        GetModAndDate(ref beginDate, ref endDate, referenceDate, durationResult.Timex, true, out mod);
                    }
                    else
                    {
                        var suffixMatch = config.FutureRegex.Match(afterStr);
                        if (suffixMatch.Success)
                        {
                            GetModAndDate(ref beginDate, ref endDate, referenceDate, durationResult.Timex, true, out mod);
                        }
                    }

                    var futureSuffixMatch = config.FutureSuffixRegex.Match(afterStr);
                    if (futureSuffixMatch.Success)
                    {
                        GetModAndDate(ref beginDate, ref endDate, referenceDate, durationResult.Timex, true, out mod);
                    }

                    // Handle the "in two weeks" case which means the second week
                    prefixMatch = config.InConnectorRegex.Match(beforeStr);
                    if (prefixMatch.Success && prefixMatch.Length == beforeStr.Length && 
                        !DurationParsingUtil.IsMultipleDuration(durationResult.Timex))
                    {
                        GetModAndDate(ref beginDate, ref endDate, referenceDate, durationResult.Timex, true, out mod);

                        // Change the duration value and the beginDate
                        var unit = durationResult.Timex.Substring(durationResult.Timex.Length - 1);

                        durationResult.Timex = "P1" + unit;
                        beginDate = DurationParsingUtil.ShiftDateTime(durationResult.Timex, endDate, false);
                    }

                    if (!string.IsNullOrEmpty(mod))
                    {
                        ((DateTimeResolutionResult) pr.Value).Mod = mod;
                    }

                    timex = durationResult.Timex;

                    ret.SubDateTimeEntities = new List<object> { pr };
                }
            }
            
            // Parse "rest of"
            var match = this.config.RestOfDateRegex.Match(text);
            if (match.Success)
            {
                var durationStr = match.Groups["duration"].Value;
                var durationUnit = this.config.UnitMap[durationStr];
                switch (durationUnit)
                {
                    case "W":
                        var diff = 7 - (((int)beginDate.DayOfWeek) == 0? 7: (int)beginDate.DayOfWeek);
                        endDate = beginDate.AddDays(diff);
                        timex = "P" + diff + "D";
                        if (diff == 0)
                        {
                            restNowSunday = true;
                        }
                        break;

                    case "MON":
                        endDate = DateObject.MinValue.SafeCreateFromValue(beginDate.Year, beginDate.Month, 1);
                        endDate = endDate.AddMonths(1).AddDays(-1);
                        diff = endDate.Day - beginDate.Day + 1;
                        timex = "P" + diff + "D";
                        break;

                    case "Y":
                        endDate = DateObject.MinValue.SafeCreateFromValue(beginDate.Year, 12, 1);
                        endDate = endDate.AddMonths(1).AddDays(-1);
                        diff = endDate.DayOfYear - beginDate.DayOfYear + 1;
                        timex = "P" + diff + "D";
                        break;
                }
            }

            if (!beginDate.Equals(endDate) || restNowSunday)
            {
                endDate = InclusiveEndPeriod ? endDate.AddDays(-1) : endDate;

                ret.Timex =
                    $"({FormatUtil.LuisDate(beginDate)},{FormatUtil.LuisDate(endDate)},{timex})";
                ret.FutureValue = ret.PastValue = new Tuple<DateObject, DateObject>(beginDate, endDate);
                ret.Success = true;

                return ret;
            }

            return ret;
        }

        private void GetModAndDate(ref DateObject beginDate, ref DateObject endDate, DateObject referenceDate, string timex, bool future, out string mod)
        {
            if (future)
            {
                mod = Constants.AFTER_MOD;

                // For future the beginDate should add 1 first
                beginDate = referenceDate.AddDays(1);
                endDate = DurationParsingUtil.ShiftDateTime(timex, beginDate, true);
            }
            else
            {
                mod = Constants.BEFORE_MOD;
                beginDate = DurationParsingUtil.ShiftDateTime(timex, endDate, false);
            }
        }

        private DateTimeResolutionResult ParseWeekOfMonth(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();

            var trimedText = text.Trim().ToLowerInvariant();
            var match = this.config.WeekOfMonthRegex.Match(trimedText);
            if (!(match.Success && match.Length == text.Length))
            {
                return ret;
            }

            var cardinalStr = match.Groups["cardinal"].Value;
            var monthStr = match.Groups["month"].Value;
            var noYear = false;
            int year;

            int cardinal;
            if (this.config.IsLastCardinal(cardinalStr))
            {
                cardinal = 5;
            }
            else
            {
                cardinal = this.config.CardinalMap[cardinalStr];
            }

            int month;
            if (string.IsNullOrEmpty(monthStr))
            {
                var swift = this.config.GetSwiftDayOrMonth(trimedText);

                month = referenceDate.AddMonths(swift).Month;
                year = referenceDate.AddMonths(swift).Year;
            }
            else
            {
                month = this.config.MonthOfYear[monthStr];
                year = referenceDate.Year;
                noYear = true;
            }

            ret = GetWeekOfMonth(cardinal, month, year, referenceDate, noYear);

            return ret;
        }

        private DateTimeResolutionResult ParseWeekOfYear(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            var trimedText = text.Trim().ToLowerInvariant();
            var match = this.config.WeekOfYearRegex.Match(trimedText);
            if (!(match.Success && match.Length == text.Length))
            {
                return ret;
            }

            var cardinalStr = match.Groups["cardinal"].Value;
            var orderStr = match.Groups["order"].Value.ToLower();

            int year = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(match);
            if (year == Constants.InvalidYear)
            {
                var swift = this.config.GetSwiftYear(orderStr);
                if (swift < -1)
                {
                    return ret;
                }
                year = referenceDate.Year + swift;
            }

            DateObject targetWeekMonday;
            int weekNum = 0;
            if (this.config.IsLastCardinal(cardinalStr))
            {
                var lastDay = DateObject.MinValue.SafeCreateFromValue(year, 12, 31);
                DateObject lastDayWeekMonday = lastDay.This(DayOfWeek.Monday);
                weekNum = Cal.GetWeekOfYear(lastDay, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                if (weekNum == 1)
                {
                    lastDayWeekMonday = lastDay.AddDays(-7).This(DayOfWeek.Monday);
                }
                targetWeekMonday = lastDayWeekMonday;
                weekNum = Cal.GetWeekOfYear(targetWeekMonday, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

                ret.Timex = $"{year.ToString("D4")}-{targetWeekMonday.Month.ToString("D2")}-W{weekNum}";
            }
            else
            {
                var firstDay = DateObject.MinValue.SafeCreateFromValue(year, 1, 1);
                DateObject firstDayWeekMonday = firstDay.This(DayOfWeek.Monday);
                
                weekNum = Cal.GetWeekOfYear(firstDay, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                if (weekNum != 1)
                {
                    firstDayWeekMonday = firstDay.AddDays(7).This(DayOfWeek.Monday);
                }
                
                var cardinal = this.config.CardinalMap[cardinalStr];
                targetWeekMonday = firstDayWeekMonday.AddDays(7 * (cardinal-1));
                var targetWeekSunday = targetWeekMonday.This(DayOfWeek.Sunday);

                ret.Timex = $"{year.ToString("D4")}-{targetWeekSunday.Month.ToString("D2")}-W{cardinal.ToString("D2")}";
            }

            ret.FutureValue = InclusiveEndPeriod
                ? new Tuple<DateObject, DateObject>(targetWeekMonday, targetWeekMonday.AddDays(6))
                : new Tuple<DateObject, DateObject>(targetWeekMonday, targetWeekMonday.AddDays(7));

            ret.PastValue = InclusiveEndPeriod
                ? new Tuple<DateObject, DateObject>(targetWeekMonday, targetWeekMonday.AddDays(6))
                : new Tuple<DateObject, DateObject>(targetWeekMonday, targetWeekMonday.AddDays(7));

            ret.Success = true;

            return ret;
        }

        private DateTimeResolutionResult ParseQuarter(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            var match = this.config.QuarterRegex.Match(text);

            if (!(match.Success && match.Length == text.Length))
            {
                match = this.config.QuarterRegexYearFront.Match(text);
            }

            if (!(match.Success && match.Length == text.Length))
            {
                return ret;
            }

            var cardinalStr = match.Groups["cardinal"].Value.ToLower();
            var orderStr = match.Groups["order"].Value.ToLower();

            int year = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(match);
            if (year == Constants.InvalidYear)
            {
                var swift = this.config.GetSwiftYear(orderStr);
                if (swift < -1)
                {
                    return ret;
                }
                year = referenceDate.Year + swift;
            }

            var quarterNum = this.config.CardinalMap[cardinalStr];
            var beginDate = DateObject.MinValue.SafeCreateFromValue(year, quarterNum * 3 - 2, 1);
            var endDate = DateObject.MinValue.SafeCreateFromValue(year, quarterNum * 3 + 1, 1);
            ret.FutureValue = ret.PastValue = new Tuple<DateObject, DateObject>(beginDate, endDate);
            ret.Timex = $"({FormatUtil.LuisDate(beginDate)},{FormatUtil.LuisDate(endDate)},P3M)";
            ret.Success = true;

            return ret;
        }

        private DateTimeResolutionResult ParseSeason(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            var match = this.config.SeasonRegex.Match(text);
            if (match.Success && match.Length == text.Length)
            {
                var seasonStr = this.config.SeasonMap[match.Groups["seas"].Value.ToLowerInvariant()];
                var orderStr = match.Groups["order"].Value.ToLower();

                if (match.Groups["EarlyPrefix"].Success)
                {
                    ret.Mod = Constants.EARLY_MOD;
                }
                else if (match.Groups["MidPrefix"].Success)
                {
                    ret.Mod = Constants.MID_MOD;
                }
                else if (match.Groups["LatePrefix"].Success)
                {
                    ret.Mod = Constants.LATE_MOD;
                }

                int year = ((BaseDateExtractor)this.config.DateExtractor).GetYearFromText(match);
                if (year == Constants.InvalidYear)
                {
                    var swift = this.config.GetSwiftYear(text);
                    if (swift < -1)
                    {
                        ret.Timex = seasonStr;
                        ret.Success = true;
                        return ret;
                    }
                    year = referenceDate.Year + swift;
                }

                var yearStr = year.ToString("D4");
                ret.Timex = yearStr + "-" + seasonStr;

                ret.Success = true;
                return ret;
            }
            return ret;
        }

        private DateTimeResolutionResult ParseWeekOfDate(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            var match = config.WeekOfRegex.Match(text);
            var ex = config.DateExtractor.Extract(text, referenceDate);
            if (match.Success && ex.Count==1)
            {
                var pr= (DateTimeResolutionResult)config.DateParser.Parse(ex[0], referenceDate).Value;
                if ((config.Options & DateTimeOptions.CalendarMode) != 0)
                {
                    var monday = ((DateObject)pr.FutureValue).This(DayOfWeek.Monday);
                    ret.Timex = FormatUtil.ToIsoWeekTimex(monday);
                }
                else
                {
                    ret.Timex = pr.Timex;
                }
                ret.Comment = Constants.Comment_WeekOf;
                ret.FutureValue= GetWeekRangeFromDate((DateObject)pr.FutureValue);
                ret.PastValue= GetWeekRangeFromDate((DateObject)pr.PastValue);
                ret.Success = true;
            }
            return ret;
        }

        private DateTimeResolutionResult ParseMonthOfDate(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            var match = config.MonthOfRegex.Match(text);
            var ex = config.DateExtractor.Extract(text, referenceDate);
            if (match.Success && ex.Count == 1)
            {
                var pr = (DateTimeResolutionResult)config.DateParser.Parse(ex[0], referenceDate).Value;
                ret.Timex = pr.Timex;
                ret.Comment = Constants.Comment_MonthOf;
                ret.FutureValue = GetMonthRangeFromDate((DateObject)pr.FutureValue);
                ret.PastValue = GetMonthRangeFromDate((DateObject)pr.PastValue);
                ret.Success = true;
            }
            return ret;
        }

        private Tuple<DateObject, DateObject> GetWeekRangeFromDate(DateObject date)
        {
            var startDate = date.This(DayOfWeek.Monday);
            var endDate = InclusiveEndPeriod ? startDate.AddDays(6) : startDate.AddDays(7);
            return new Tuple<DateObject, DateObject>(startDate, endDate);
        }

        private Tuple<DateObject, DateObject> GetMonthRangeFromDate(DateObject date)
        {
            var startDate = DateObject.MinValue.SafeCreateFromValue(date.Year, date.Month, 1);
            DateObject endDate;
            if (date.Month < 12)
            {
                endDate = DateObject.MinValue.SafeCreateFromValue(date.Year, date.Month + 1, 1);
            }
            else
            {
                endDate = DateObject.MinValue.SafeCreateFromValue(date.Year + 1, 1, 1);
            }
            endDate = InclusiveEndPeriod ? endDate.AddDays(-1) : endDate;
            return new Tuple<DateObject, DateObject>(startDate, endDate);
        }

        private DateTimeResolutionResult ParseWhichWeek(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            var match = this.config.WhichWeekRegex.Match(text);
            if (match.Success)
            {
                var num = int.Parse(match.Groups["number"].ToString());
                int year = referenceDate.Year;
                ret.Timex = year.ToString("D4");
                var firstDay = DateObject.MinValue.SafeCreateFromValue(year, 1, 1);
                var firstWeekday = firstDay.This((DayOfWeek)1);
                var value = firstWeekday.AddDays(7 * num);
                var futureDate = value;
                var pastDate = value;
                ret.Timex += "-W" + num.ToString("D2");
                ret.FutureValue = new Tuple<DateObject, DateObject>(futureDate, futureDate.AddDays(7));
                ret.PastValue = new Tuple<DateObject, DateObject>(pastDate, pastDate.AddDays(7));
                ret.Success = true;
            }
            return ret;
        }
        
        private static DateTimeResolutionResult GetWeekOfMonth(int cardinal, int month, int year, DateObject referenceDate, bool noYear)
        {
            var ret = new DateTimeResolutionResult();
            var value = ComputeDate(cardinal, 1, month, year);
            if (value.Month != month)
            {
                cardinal -= 1;
                value = value.AddDays(-7);
            }

            var futureDate = value;
            var pastDate = value;
            if (noYear && futureDate < referenceDate)
            {
                futureDate = ComputeDate(cardinal, 1, month, year + 1);
                if (futureDate.Month != month)
                {
                    futureDate = futureDate.AddDays(-7);
                }
            }

            if (noYear && pastDate >= referenceDate)
            {
                pastDate = ComputeDate(cardinal, 1, month, year - 1);
                if (pastDate.Month != month)
                {
                    pastDate = pastDate.AddDays(-7);
                }
            }

            if (noYear)
            {
                ret.Timex = "XXXX" + "-" + month.ToString("D2");
            }
            else
            {
                ret.Timex = year.ToString("D4") + "-" + month.ToString("D2");
            }

            ret.Timex += "-W" + cardinal.ToString("D2");

            ret.FutureValue = InclusiveEndPeriod
                ? new Tuple<DateObject, DateObject>(futureDate, futureDate.AddDays(6))
                : new Tuple<DateObject, DateObject>(futureDate, futureDate.AddDays(7));

            ret.PastValue = InclusiveEndPeriod
                ? new Tuple<DateObject, DateObject>(pastDate, pastDate.AddDays(6))
                : new Tuple<DateObject, DateObject>(pastDate, pastDate.AddDays(7));

            ret.Success = true;

            return ret;
        }

        private static DateObject ComputeDate(int cardinal, int weekday, int month, int year)
        {
            var firstDay = DateObject.MinValue.SafeCreateFromValue(year, month, 1);
            var firstWeekday = firstDay.This((DayOfWeek)weekday);

            if (weekday == 0)
            {
                weekday = 7;
            }

            var firstDayOfWeek = firstDay.DayOfWeek != 0 ? (int)firstDay.DayOfWeek : 7;

            if (weekday < firstDayOfWeek)
            {
                firstWeekday = firstDay.Next((DayOfWeek)weekday);
            }

            return firstWeekday.AddDays(7 * (cardinal - 1));
        }

        private DateTimeResolutionResult ParseDecade(string text, DateObject referenceDate)
        {
            var ret = new DateTimeResolutionResult();
            int firstTwoNumOfYear = referenceDate.Year / 100;
            int decade;
            int beginYear;
            int decadeLastYear = 10;
            int swift = 1;
            var inputCentury = false;

            var trimedText = text.Trim();
            var match = this.config.DecadeWithCenturyRegex.Match(trimedText);
            string beginLuisStr, endLuisStr;

            if (match.Success && match.Index == 0 && match.Length == trimedText.Length)
            {
                var decadeStr = match.Groups["decade"].Value.ToLower();
                if (!int.TryParse(decadeStr, out decade))
                {
                    if (this.config.WrittenDecades.ContainsKey(decadeStr))
                    {
                        decade = this.config.WrittenDecades[decadeStr];
                    }
                    else if (this.config.SpecialDecadeCases.ContainsKey(decadeStr))
                    {
                        firstTwoNumOfYear = this.config.SpecialDecadeCases[decadeStr] / 100;
                        decade = this.config.SpecialDecadeCases[decadeStr] % 100;
                        inputCentury = true;
                    }
                }

                var centuryStr = match.Groups["century"].Value.ToLower();
                if (!string.IsNullOrEmpty(centuryStr))
                {
                    if (!int.TryParse(centuryStr, out firstTwoNumOfYear))
                    {
                        if (this.config.Numbers.ContainsKey(centuryStr))
                        {
                            firstTwoNumOfYear = this.config.Numbers[centuryStr];
                        }
                        else
                        {
                            // handle the case like "one/two thousand", "one/two hundred", etc.
                            var er = this.config.IntegerExtractor.Extract(centuryStr);

                            if (er.Count == 0)
                            {
                                return ret;
                            }

                            firstTwoNumOfYear = Convert.ToInt32((double)(this.config.NumberParser.Parse(er[0]).Value ?? 0));
                            if (firstTwoNumOfYear >= 100)
                            {
                                firstTwoNumOfYear = firstTwoNumOfYear / 100;
                            }
                        }
                    }

                    inputCentury = true;
                }
            }
            else
            {
                // handle cases like "the last 2 decades" "the next decade"
                match = this.config.RelativeDecadeRegex.Match(trimedText);
                if (match.Success && match.Index == 0 && match.Length == trimedText.Length)
                {
                    inputCentury = true;

                    swift = this.config.GetSwiftDayOrMonth(trimedText);

                    var numStr = match.Groups["number"].Value.ToLower();
                    var er = this.config.IntegerExtractor.Extract(numStr);
                    if (er.Count == 1)
                    {
                        var swiftNum = Convert.ToInt32((double)(this.config.NumberParser.Parse(er[0]).Value ?? 0));
                        swift = swift * swiftNum;
                    }

                    var beginDecade = (referenceDate.Year % 100) / 10;
                    if (swift < 0)
                    {
                        beginDecade += swift;
                    }
                    else if (swift > 0)
                    {
                        beginDecade += 1;
                    }

                    decade = beginDecade * 10;
                }
                else
                {
                    return ret;
                }
            }

            beginYear = firstTwoNumOfYear * 100 + decade;
            var totalLastYear = decadeLastYear * Math.Abs(swift);

            if (inputCentury)
            {
                beginLuisStr = FormatUtil.LuisDate(beginYear, 1, 1);
                endLuisStr = FormatUtil.LuisDate(beginYear + totalLastYear, 1, 1);
            }
            else
            {
                var beginYearStr = "XX" + decade.ToString();
                beginLuisStr = FormatUtil.LuisDate(-1, 1, 1);
                beginLuisStr = beginLuisStr.Replace("XXXX", beginYearStr);

                var endYearStr = "XX" + (decade + totalLastYear).ToString();
                endLuisStr = FormatUtil.LuisDate(-1, 1, 1);
                endLuisStr = endLuisStr.Replace("XXXX", endYearStr);
            }
            ret.Timex = $"({beginLuisStr},{endLuisStr},P{totalLastYear}Y)";

            int futureYear = beginYear, pastYear = beginYear;
            var startDate = DateObject.MinValue.SafeCreateFromValue(beginYear, 1, 1);
            if (!inputCentury && startDate < referenceDate)
            {
                futureYear += 100;
            }

            if (!inputCentury && startDate >= referenceDate)
            {
                pastYear -= 100;
            }

            ret.FutureValue = new Tuple<DateObject, DateObject>(
                DateObject.MinValue.SafeCreateFromValue(futureYear, 1, 1),
                DateObject.MinValue.SafeCreateFromValue(futureYear + totalLastYear, 1, 1));

            ret.PastValue = new Tuple<DateObject, DateObject>(
                DateObject.MinValue.SafeCreateFromValue(pastYear, 1, 1),
                DateObject.MinValue.SafeCreateFromValue(pastYear + totalLastYear, 1, 1));

            ret.Success = true;

            return ret;
        }

        public bool GetInclusiveEndPeriodFlag()
        {
            return InclusiveEndPeriod;
        }
    }

    public enum CalculateRangeMode
    {
        Week,
        Month
    }
}