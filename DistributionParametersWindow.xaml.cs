using System;
using System.Windows;
using System.Windows.Controls;
using DersDagitim.Models;

namespace DersDagitim;

public partial class DistributionParametersWindow : Window
{
    public DistributionParameters Parameters { get; private set; }
    
    public DistributionParametersWindow()
    {
        InitializeComponent();
        Parameters = new DistributionParameters();
        
        // Initialize labels with default values
        UpdateLabels();
    }
    
    private void UpdateLabels()
    {
        if (LabelTime != null && SliderTime != null)
            LabelTime.Text = $"{(int)SliderTime.Value} saniye";
        
        // Gap level
        if (LabelGap != null && SliderGap != null)
        {
            int gapVal = (int)SliderGap.Value;
            if (gapVal < 20) LabelGap.Text = "Kapalı";
            else if (gapVal < 50) LabelGap.Text = "Düşük";
            else if (gapVal < 80) LabelGap.Text = "Orta";
            else LabelGap.Text = "Yüksek";
        }

        // Morning
        if (LabelMorning != null && SliderMorning != null) {
            double mornVal = SliderMorning.Value;
            if (mornVal < 2) LabelMorning.Text = "Düşük";
            else if (mornVal < 5) LabelMorning.Text = "Orta";
            else LabelMorning.Text = "Yüksek";
        }
        
        // Adjacency
        if (LabelAdjacency != null && SliderAdjacency != null) {
            int adjVal = (int)SliderAdjacency.Value;
            if (adjVal < 30) LabelAdjacency.Text = "Düşük";
            else if (adjVal < 70) LabelAdjacency.Text = "Orta";
            else LabelAdjacency.Text = "Yüksek";
        }
        
        // Min daily
        if (LabelMinDaily != null && SliderMinDaily != null) LabelMinDaily.Text = $"{(int)SliderMinDaily.Value} ders";

        // Balance
        if (LabelBalance != null && SliderBalance != null) {
            int balVal = (int)SliderBalance.Value;
            if (balVal < 30) LabelBalance.Text = "Düşük";
            else if (balVal < 70) LabelBalance.Text = "Orta";
            else LabelBalance.Text = "Yüksek";
        }
    }
    
    private void SliderTime_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (LabelTime != null) UpdateLabels(); }
    private void SliderGap_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (LabelGap != null) UpdateLabels(); }
    private void SliderMorning_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (LabelMorning != null) UpdateLabels(); }
    private void SliderAdjacency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (LabelAdjacency != null) UpdateLabels(); }
    private void SliderMinDaily_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (LabelMinDaily != null) UpdateLabels(); }
    private void SliderBalance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (LabelBalance != null) UpdateLabels(); }
    
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        // Map slider values to parameters
        Parameters.MaxTimeInSeconds = (int)SliderTime.Value;
        
        // Checkboxes
        Parameters.UseStrictMode = CheckStrict.IsChecked == true;
        Parameters.UseAnalysisMode = CheckAnalysis.IsChecked == true;
        Parameters.UseExternalPython = false; // Default native engine
        
        // Robust Check for Nullable Boolean
        bool isMinimizeChecked = CheckMinimizeDays.IsChecked.HasValue && CheckMinimizeDays.IsChecked.Value;
        Parameters.MinimizeWorkingDays = isMinimizeChecked;
        
        // Sliders -> Parameters
        Parameters.GapPenalty = (int)SliderGap.Value;
        Parameters.MorningPenalty = SliderMorning.Value;
        Parameters.AdjacencyReward = (int)(SliderAdjacency.Value * 200); // +20K at max
        Parameters.MinDailyLessons = (int)SliderMinDaily.Value;
        Parameters.BalancePenalty = (int)(SliderBalance.Value * 2); // 0-100 -> 0-200 like Swift (-100 equiv)
        
        // Mode Selection
        Parameters.OperationMode = OperationMode.Rebuild;
        
        if (ComboResetMode.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (tag == "ClearAll") Parameters.PlacementMode = PlacementMode.ClearAll;
            else if (tag == "KeepLocked") Parameters.PlacementMode = PlacementMode.KeepLocked;
            else if (tag == "KeepCurrent") Parameters.PlacementMode = PlacementMode.KeepCurrent;
        }
        else
        {
            Parameters.PlacementMode = PlacementMode.ClearAll;
        }
        

        
        DialogResult = true;
        Close();
    }
}
