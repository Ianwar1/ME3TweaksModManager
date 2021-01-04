﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MassEffectModManagerCore.modmanager.objects;
using MassEffectModManagerCore.ui;

namespace MassEffectModManagerCore.modmanager.usercontrols
{
    /// <summary>
    /// Interaction logic for TargetSelector.xaml
    /// </summary>
    public partial class TargetSelector : UserControl, INotifyPropertyChanged
    {
        #region ShowTextureInfo DP

        /// <summary>
        /// Sets if this selector should show the texture info or not. Typically in space constrained scenarios this can be hidden
        /// </summary>
        public bool ShowTextureInfo
        {
            get => (bool)GetValue(ShowTextureInfoProperty);
            set => SetValue(ShowTextureInfoProperty, value);
        }

        /// <summary>
        /// Identified the Label dependency property
        /// </summary>
        public static readonly DependencyProperty ShowTextureInfoProperty =
            DependencyProperty.Register(@"ShowTextureInfo", typeof(bool),
                typeof(TargetSelector), new PropertyMetadata(true));

        #endregion

        #region SelectedGameTarget DP

        /// <summary>
        /// The current selected game target
        /// </summary>
        public GameTarget SelectedGameTarget
        {
            get => (GameTarget)GetValue(SelectedGameTargetProperty);
            set => SetValue(SelectedGameTargetProperty, value);
        }

        /// <summary>
        /// Which target is selected
        /// </summary>
        public static readonly DependencyProperty SelectedGameTargetProperty =
            DependencyProperty.Register(@"SelectedGameTarget", typeof(GameTarget),
                typeof(TargetSelector), new FrameworkPropertyMetadata(
                    null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        #endregion

        #region Available Targets DP

        /// <summary>
        /// The current selected game target
        /// </summary>
        public ObservableCollectionExtended<GameTarget> AvailableTargets
        {
            get => (ObservableCollectionExtended<GameTarget>)GetValue(AvailableTargetsProperty);
            set => SetValue(AvailableTargetsProperty, value);
        }

        /// <summary>
        /// Identified the Label dependency property
        /// </summary>
        public static readonly DependencyProperty AvailableTargetsProperty =
            DependencyProperty.Register(@"AvailableTargets", typeof(ObservableCollectionExtended<GameTarget>),
                typeof(TargetSelector), new PropertyMetadata(new ObservableCollectionExtended<GameTarget>()));

        #endregion

        #region Theme DP

        public enum TargetSelectorTheme
        {
            Normal,
            Accent
        }

        /// <summary>
        /// Sets if this selector should show the texture info or not. Typically in space constrained scenarios this can be hidden
        /// </summary>
        public TargetSelectorTheme Theme
        {
            get => (TargetSelectorTheme)GetValue(ThemeProperty);
            set
            {
                SetValue(ThemeProperty, value);
                if (value == TargetSelectorTheme.Normal)
                {
                    ContainerStyle = (Style)FindResource(@"TargetSelectorContainerStyle");
                }
                else if (value == TargetSelectorTheme.Accent)
                {
                    ContainerStyle = (Style)FindResource(@"TargetSelectorContainerAccentStyle");
                }
                else
                {
                    throw new Exception($@"{value} is not a valid value for TargetSelector Theme property!");
                }
            }
        }

        /// <summary>
        /// The theme of the TargetSelector. Normal is default, Accent makes it blue.
        /// </summary>
        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.Register(@"Theme", typeof(TargetSelectorTheme),
                typeof(TargetSelector), new PropertyMetadata(TargetSelectorTheme.Normal));

        #endregion

        public Style ContainerStyle { get; set; }
        public TargetSelector()
        {
            InitializeComponent();
        }

        //Fody uses this property on weaving
#pragma warning disable
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore
    }

    public class ExtendedTextureInfoVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GameTarget gt && parameter is TargetSelector ts)
            {
                return (ts.ShowTextureInfo && gt.TextureModded) ? Visibility.Visible : Visibility.Collapsed;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return false; //don't need this
        }
    }
}
