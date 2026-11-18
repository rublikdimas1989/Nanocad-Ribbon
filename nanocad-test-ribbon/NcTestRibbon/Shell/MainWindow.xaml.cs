namespace NcTestRibbon.Shell
{
  using System.Windows;

  public partial class MainWindow : Window
  {
    public MainWindow()
    {
      InitializeComponent();

    }

private void AnalyzePathByNumber_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.AnalyzePathByNumber_Click");
    }

private void ApplyAppearance_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ApplyAppearance_Click");
    }

private void ApplyGoal_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ApplyGoal_Click");
    }

private void ApplyPrep_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ApplyPrep_Click");
    }

private void ApplyStart_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ApplyStart_Click");
    }

private void ApplyZoom_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ApplyZoom_Click");
    }

private void ZoomTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
      // Stub: allow digits only later; for now do not block input.
      e.Handled = false;
    }

private void ZoomTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key == System.Windows.Input.Key.Enter)
      {
        StubUi.Notify(this, "MainWindow.ZoomTextBox_KeyDown(Enter)");
        e.Handled = true;
      }
    }

private void OpacityTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
      // Stub: numeric filter not enforced in shell UI.
      e.Handled = false;
    }

private void BtnApplyRouteEdits_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnApplyRouteEdits_Click");
    }

private void BtnDefaultPoints_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnDefaultPoints_Click");
    }

private void BtnDeleteRoutePoint_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnDeleteRoutePoint_Click");
    }

private void BtnDrawLasso_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnDrawLasso_Click");
    }

private void BtnEditRoute_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnEditRoute_Click");
    }

private void BtnInsertRoutePoint_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnInsertRoutePoint_Click");
    }

private void BtnNormalizeRoute_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnNormalizeRoute_Click");
    }

private void BtnPickGoal_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnPickGoal_Click");
    }

private void BtnPickStart_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.BtnPickStart_Click");
    }

private void CancelRoadGeneration_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.CancelRoadGeneration_Click");
    }

private void ChkShowLasso_Toggled(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ChkShowLasso_Toggled");
    }

private void ClearGroupSelection_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ClearGroupSelection_Click");
    }

private void ClearScreen_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ClearScreen_Click");
    }

private void Clear_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.Clear_Click");
    }

private void CloseMapToolsPane_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.CloseMapToolsPane_Click");
    }

private void Downsample_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.Downsample_Changed");
    }

private void EcwLayerOpacity_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.EcwLayerOpacity_TextChanged");
    }

private void EcwLayerVisibility_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.EcwLayerVisibility_Changed");
    }

private void EditRoad_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.EditRoad_Click");
    }

private void EditSelectedGroup_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.EditSelectedGroup_Click");
    }

private void ExternalLayerOpacity_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ExternalLayerOpacity_TextChanged");
    }

private void ExternalLayerVisibility_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ExternalLayerVisibility_Changed");
    }

private void GenerateRoadPipes_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.GenerateRoadPipes_Click");
    }

private void GenerateSelectedRoadPipes_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.GenerateSelectedRoadPipes_Click");
    }

private void GroupSelectionHint_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.GroupSelectionHint_Click");
    }

private void InfrastructureLayersPanel_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.InfrastructureLayersPanel_Loaded");
    }

private void LayerToggle_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.LayerToggle_Changed");
    }

private void MapCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapCanvas_MouseDown");
    }

private void MapCanvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapCanvas_MouseLeave");
    }

private void MapCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapCanvas_MouseLeftButtonDown");
    }

private void MapCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapCanvas_MouseLeftButtonUp");
    }

private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapCanvas_MouseMove");
    }

private void MapCanvas_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapCanvas_MouseRightButtonDown");
    }

private void MapCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapCanvas_MouseUp");
    }

private void MapDrawTool_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapDrawTool_Click");
    }

private void MapScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapScrollViewer_PreviewMouseWheel");
    }

private void MapToolsTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.MapToolsTabs_SelectionChanged");
    }

private void OpacityTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.OpacityTextBox_TextChanged");
    }

private void OpenInitialSettingsWindow_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      var dlg = new SettingsWindow();
      dlg.Owner = this;
      dlg.ShowDialog();
    }

private void OpenRouteDxf2D_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.OpenRouteDxf2D_Click");
    }

private void OpenRouteDxf3D_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.OpenRouteDxf3D_Click");
    }

private void OpenRouteWeb_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.OpenRouteWeb_Click");
    }

private void OpenScene_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.OpenScene_Click");
    }

private void PathDisplayMode_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.PathDisplayMode_Changed");
    }

private void Prep_calc(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.Prep_calc");
    }

private void ResetMapDrawingEdits_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ResetMapDrawingEdits_Click");
    }

private void ResetZoom_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ResetZoom_Click");
    }

private void RouteWebVariant_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.RouteWebVariant_SelectionChanged");
    }

private void Run_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.Run_Click");
    }

private void SavePathDxf2D_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.SavePathDxf2D_Click");
    }

private void SavePathDxf3D_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.SavePathDxf3D_Click");
    }

private void SaveProfileDxf_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.SaveProfileDxf_Click");
    }

private void SaveRouteWeb_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.SaveRouteWeb_Click");
    }

private void SaveScene_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.SaveScene_Click");
    }

private void ShowAppearancePane_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ShowAppearancePane_Click");
    }

private void ShowFinalSummary_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ShowFinalSummary_Click");
    }

private void ShowLayersPane_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ShowLayersPane_Click");
    }

private void ShowProfile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ShowProfile_Click");
    }

private void ShowRouteWeb_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ShowRouteWeb_Click");
    }

private void SiteGridVisibility_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.SiteGridVisibility_Changed");
    }

private void SiteGrid_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.SiteGrid_Click");
    }

private void ToggleSidePanel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.ToggleSidePanel_Click");
    }

private void TopologyBuosSites_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyBuosSites_Click");
    }

private void TopologyCallouts_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyCallouts_Click");
    }

private void TopologyCancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyCancel_Click");
    }

private void TopologyExportDxf_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyExportDxf_Click");
    }

private void TopologyFreeTrace_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyFreeTrace_Click");
    }

private void TopologyFullTrace_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyFullTrace_Click");
    }

private void TopologyInputs_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyInputs_Click");
    }

private void TopologyLayerVisibility_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyLayerVisibility_Changed");
    }

private void TopologyLoadHistory_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyLoadHistory_Click");
    }

private void TopologyOptimization_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyOptimization_Click");
    }

private void TopologyQuick_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyQuick_Click");
    }

private void TopologyRefine_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyRefine_Click");
    }

private void TopologyResults_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyResults_Click");
    }

private void TopologySearch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologySearch_Click");
    }

private void TopologySettings_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologySettings_Click");
    }

private void TopologyShowPads_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyShowPads_Click");
    }

private void TopologyTieIns_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyTieIns_Click");
    }

private void TopologyVariant_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "MainWindow.TopologyVariant_SelectionChanged");
    }
  }
}
