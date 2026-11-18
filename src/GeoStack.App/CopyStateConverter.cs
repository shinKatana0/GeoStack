// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GeoStack.App;

public sealed class CopyStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? Strings.Get(b ? "Yes" : "No") : Strings.Get(value?.ToString() ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
