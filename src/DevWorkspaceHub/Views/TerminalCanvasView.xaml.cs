using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DevWorkspaceHub.Controls;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Views;

/// <summary>
/// Canvas infinito de terminais lado a lado.
/// Suporta:
///   - Arrastar para navegar (clique no fundo/barra do card)
///   - Ctrl+Scroll para zoom (centralizado no cursor)
///   - Duplo-clique em um card para focar nele (animação suave)
///   - Botão "Ver Todos" para voltar à visão geral
/// </summary>
public partial class TerminalCanvasView : UserControl
{
    // ─── Constantes ──────────────────────────────────────────────────────────

    private const double CardWidth     = 620;
    private const double CardHeight    = 420;
    private const double CardGap       = 16;
    private const double CardPadding   = 24;   // margem do WrapPanel
    private const double MinZoom       = 0.25;
    private const double MaxZoom       = 2.0;
    private const double ZoomStep      = 0.12;

    // ─── Estado de drag ──────────────────────────────────────────────────────

    private bool    _isDragging;
    private Point   _dragStart;
    private double  _dragOriginX;
    private double  _dragOriginY;
    private bool    _dragMoved;       // distingue click de drag

    // ─── Estado de foco ──────────────────────────────────────────────────────

    private bool    _isFocused;       // está no modo "foco" (1 terminal ampliado)
    private TerminalViewModel? _focusedTerminal;

    // ─── ViewModel de referência ─────────────────────────────────────────────

    private MainViewModel? _mainVm;

    // ─── Construtor ──────────────────────────────────────────────────────────

    public TerminalCanvasView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ─── Inicialização ───────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Garantir resolução do MainViewModel via Window (DataContext ainda pode não estar setado)
        ResolveOnLoad();
        // Configurar largura do WrapPanel para wrap correto
        UpdateWrapPanelWidth();
        // Centralizar canvas inicialmente
        CenterCanvas();
        UpdateEmptyState();

        // Atualizar wrap quando o CanvasArea muda de tamanho
        CanvasArea.SizeChanged += (_, _) => UpdateWrapPanelWidth();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Desconectar do ViewModel anterior
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.Terminals.CollectionChanged -= OnTerminalsChanged;
        }

        // O DataContext do UserControl é o DataContext do Window (MainViewModel)
        // mas o TerminalView não define DataContext, usa o do Window diretamente.
        // Verificar se o DataContext já é o MainViewModel ou obtê-lo via Window.
        ResolveMainViewModel();
    }

    private void ResolveMainViewModel()
    {
        var window = Window.GetWindow(this);
        _mainVm = window?.DataContext as MainViewModel;

        if (_mainVm != null)
        {
            _mainVm.Terminals.CollectionChanged -= OnTerminalsChanged; // seguro chamar mesmo sem registro
            _mainVm.Terminals.CollectionChanged += OnTerminalsChanged;

            // Sincronizar terminais existentes
            RefreshTerminalCards();
        }
    }

    // ─── Sincronização de terminais ──────────────────────────────────────────

    private void OnTerminalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                    foreach (TerminalViewModel tvm in e.NewItems)
                        AddTerminalCard(tvm);
                UpdateEmptyState();
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                    foreach (TerminalViewModel tvm in e.OldItems)
                        RemoveTerminalCard(tvm);
                UpdateEmptyState();
                // Se estávamos focados no terminal removido, voltar à visão geral
                if (_isFocused && _focusedTerminal != null && e.OldItems!.Contains(_focusedTerminal))
                    ExitFocusMode(animated: false);
                break;

            case NotifyCollectionChangedAction.Reset:
                RefreshTerminalCards();
                UpdateEmptyState();
                break;
        }
    }

    private void RefreshTerminalCards()
    {
        TerminalsPanel.Children.Clear();

        if (_mainVm == null) return;

        foreach (var tvm in _mainVm.Terminals)
            AddTerminalCard(tvm);

        UpdateEmptyState();
    }

    // ─── Card de terminal ────────────────────────────────────────────────────

    private void AddTerminalCard(TerminalViewModel tvm)
    {
        // Estrutura do card:
        //   Border (card) → Grid
        //     Row 0: barra de título (draggável / duplo-clique)
        //     Row 1: TerminalControl
        var card = new Border
        {
            Width              = CardWidth,
            Height             = CardHeight,
            Background         = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x25)),  // MantelBg
            BorderBrush        = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),  // Surface0
            BorderThickness    = new Thickness(1),
            CornerRadius       = new CornerRadius(8),
            Margin             = new Thickness(CardGap / 2),
            DataContext        = tvm,
            ClipToBounds       = true,
            Tag                = tvm    // referência rápida
        };

        // Sem DropShadowEffect — é muito pesado em WPF com múltiplos cards.
        // A borda já diferencia os cards do fundo.

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Barra de título ──────────────────────────────────────────────────
        var titleBar = new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x1B)),  // CrustBg
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),  // Surface0
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor          = Cursors.SizeAll,
            Tag             = tvm   // referência rápida
        };

        // Duplo-clique na barra = focar terminal
        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                EnterFocusMode(tvm);
                e.Handled = true;
            }
            // Clique simples na barra inicia drag (será tratado pelo canvas, não markamos Handled)
        };

        // Conteúdo da barra de título
        var titleDock = new DockPanel { Margin = new Thickness(10, 0, 4, 0) };

        // Status dot
        var statusDot = new System.Windows.Shapes.Ellipse
        {
            Width              = 7,
            Height             = 7,
            VerticalAlignment  = VerticalAlignment.Center,
            Margin             = new Thickness(0, 0, 7, 0)
        };
        // Bind da cor do status via binding simples
        var dotBinding = new System.Windows.Data.Binding("Session.Status")
        {
            Source    = tvm,
            Converter = (System.Windows.Data.IValueConverter?)FindResource("StatusToColor")
        };
        statusDot.SetBinding(System.Windows.Shapes.Ellipse.FillProperty, dotBinding);

        DockPanel.SetDock(statusDot, Dock.Left);
        titleDock.Children.Add(statusDot);

        // Botão fechar (DockPanel.Dock = Right)
        var closeBtn = new Button
        {
            Style              = (Style)FindResource("IconButton"),
            Width              = 24,
            Height             = 24,
            VerticalAlignment  = VerticalAlignment.Center,
            ToolTip            = "Fechar Terminal"
        };
        var closePath = new System.Windows.Shapes.Path
        {
            Data            = (Geometry)FindResource("CloseIcon"),
            Width           = 8,
            Height          = 8,
            Stroke          = (Brush)FindResource("Overlay0Brush"),
            StrokeThickness = 2,
            Stretch         = Stretch.Uniform
        };
        closeBtn.Content = closePath;
        closeBtn.Click  += (s, e) =>
        {
            e.Handled = true;
            _mainVm?.CloseTerminalCommand.Execute(tvm);
        };

        DockPanel.SetDock(closeBtn, Dock.Right);
        titleDock.Children.Add(closeBtn);

        // Título
        var titleText = new TextBlock
        {
            Foreground         = (Brush)FindResource("SubtextBrush"),
            FontSize           = 12,
            VerticalAlignment  = VerticalAlignment.Center,
            TextTrimming       = TextTrimming.CharacterEllipsis
        };
        var titleBinding = new System.Windows.Data.Binding("Title") { Source = tvm };
        titleText.SetBinding(TextBlock.TextProperty, titleBinding);
        titleDock.Children.Add(titleText);

        titleBar.Child = titleDock;
        Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        // ── TerminalControl ──────────────────────────────────────────────────
        var terminalControl = new TerminalControl
        {
            DataContext = tvm
        };
        Grid.SetRow(terminalControl, 1);
        grid.Children.Add(terminalControl);

        card.Child = grid;

        // Duplo-clique no conteúdo do terminal também foca
        terminalControl.MouseDoubleClick += (s, e) =>
        {
            if (!_isFocused || _focusedTerminal != tvm)
            {
                EnterFocusMode(tvm);
                e.Handled = true;
            }
        };

        TerminalsPanel.Children.Add(card);
    }

    private void RemoveTerminalCard(TerminalViewModel tvm)
    {
        var toRemove = TerminalsPanel.Children
            .OfType<Border>()
            .FirstOrDefault(b => b.Tag == tvm);

        if (toRemove != null)
            TerminalsPanel.Children.Remove(toRemove);
    }

    private void UpdateEmptyState()
    {
        bool hasTerminals = TerminalsPanel.Children.Count > 0;
        EmptyState.Visibility   = hasTerminals ? Visibility.Collapsed : Visibility.Visible;
        RootCanvas.Visibility   = hasTerminals ? Visibility.Visible   : Visibility.Collapsed;
    }

    /// <summary>
    /// Define a largura do WrapPanel para que os cards façam wrap corretamente.
    /// Calcula quantas colunas cabem na viewport e ajusta.
    /// </summary>
    private void UpdateWrapPanelWidth()
    {
        double viewW = CanvasArea.ActualWidth;
        if (viewW <= 0) viewW = 1200;

        double cardTotalW = CardWidth + CardGap;  // 620 + 16 = 636
        int cols = Math.Max(1, (int)((viewW - 2 * CardPadding) / cardTotalW));
        double panelW = cols * cardTotalW + 2 * CardPadding;

        TerminalsPanel.Width = panelW;
    }

    // ─── Drag para navegar ───────────────────────────────────────────────────

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Só iniciar drag se clicar no fundo do canvas ou na barra de título dos cards
        // NÃO iniciar drag se clicar dentro do TerminalControl
        if (!IsDragTarget(e.Source))
            return;

        if (e.ChangedButton != MouseButton.Left) return;

        _isDragging   = true;
        _dragMoved    = false;
        _dragStart    = e.GetPosition(CanvasArea);
        _dragOriginX  = CanvasTranslate.X;
        _dragOriginY  = CanvasTranslate.Y;

        Mouse.Capture(CanvasArea);
        CanvasArea.Cursor = Cursors.SizeAll;
        e.Handled = false;   // não bloquear eventos filhos
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var current = e.GetPosition(CanvasArea);
        var deltaX  = current.X - _dragStart.X;
        var deltaY  = current.Y - _dragStart.Y;

        if (Math.Abs(deltaX) > 3 || Math.Abs(deltaY) > 3)
            _dragMoved = true;

        CanvasTranslate.X = _dragOriginX + deltaX;
        CanvasTranslate.Y = _dragOriginY + deltaY;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        Mouse.Capture(null);
        CanvasArea.Cursor = Cursors.Arrow;
    }

    /// <summary>
    /// Verifica se o elemento clicado é um alvo válido para drag
    /// (fundo do canvas ou barra de título dos cards, NÃO o conteúdo do terminal).
    /// </summary>
    private bool IsDragTarget(object source)
    {
        if (source is TerminalControl) return false;

        // Percorrer a árvore visual até encontrar o CanvasArea ou um TerminalControl
        var element = source as DependencyObject;
        while (element != null)
        {
            if (element is TerminalControl)   return false;  // dentro do terminal → não arrastar
            if (element is RichTextBox)       return false;
            if (element is TextBox)           return false;
            if (element == CanvasArea)        return true;   // chegou ao fundo → pode arrastar
            element = VisualTreeHelper.GetParent(element);
        }

        return true;
    }

    // ─── Zoom com Ctrl+Scroll ────────────────────────────────────────────────

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            return;

        e.Handled = true;

        double oldScale = CanvasScale.ScaleX;
        double delta    = e.Delta > 0 ? ZoomStep : -ZoomStep;
        double newScale = Math.Clamp(oldScale + delta, MinZoom, MaxZoom);

        if (Math.Abs(newScale - oldScale) < 0.001) return;

        // Zoom centralizado no cursor: ajustar TranslateTransform para que o ponto
        // sob o cursor permaneça no mesmo lugar na tela
        var mousePos = e.GetPosition(CanvasArea);  // posição do mouse no CanvasArea

        // Posição do mouse relativa ao canvas (antes do zoom)
        double canvasX = (mousePos.X - CanvasTranslate.X) / oldScale;
        double canvasY = (mousePos.Y - CanvasTranslate.Y) / oldScale;

        // Novo TranslateTransform para manter o ponto sob o cursor
        CanvasTranslate.X = mousePos.X - canvasX * newScale;
        CanvasTranslate.Y = mousePos.Y - canvasY * newScale;

        CanvasScale.ScaleX = newScale;
        CanvasScale.ScaleY = newScale;

        UpdateZoomLabel(newScale);
    }

    private void UpdateZoomLabel(double scale)
    {
        ZoomLabel.Text = $"{(int)Math.Round(scale * 100)}%";
    }

    // ─── Modo foco (zoom em um terminal específico) ──────────────────────────

    private void EnterFocusMode(TerminalViewModel tvm)
    {
        _isFocused       = true;
        _focusedTerminal = tvm;

        // Mostrar botão "Ver Todos"
        BtnVerTodos.Visibility = Visibility.Visible;

        // Calcular posição do card no WrapPanel
        var card = TerminalsPanel.Children
            .OfType<Border>()
            .FirstOrDefault(b => b.Tag == tvm);

        if (card == null) return;

        // Obter posição do card relativo ao RootCanvas
        var cardPos = card.TranslatePoint(new Point(0, 0), RootCanvas);

        // Dimensões da área visível
        double viewW = CanvasArea.ActualWidth;
        double viewH = CanvasArea.ActualHeight;

        // Escala alvo: ocupar 90% da área visível mantendo proporção
        double targetScaleX = (viewW * 0.90) / CardWidth;
        double targetScaleY = (viewH * 0.90) / CardHeight;
        double targetScale  = Math.Min(targetScaleX, targetScaleY);
        targetScale = Math.Clamp(targetScale, MinZoom, MaxZoom);

        // Centro do card na escala alvo
        double scaledCardCenterX = (cardPos.X + CardWidth  / 2) * targetScale;
        double scaledCardCenterY = (cardPos.Y + CardHeight / 2) * targetScale;

        double targetTransX = viewW / 2 - scaledCardCenterX;
        double targetTransY = viewH / 2 - scaledCardCenterY;

        AnimateCanvasTo(targetScale, targetTransX, targetTransY);
    }

    private void ExitFocusMode(bool animated = true)
    {
        _isFocused       = false;
        _focusedTerminal = null;

        BtnVerTodos.Visibility = Visibility.Collapsed;

        if (animated)
            AnimateVerTodos();
        else
            CenterCanvas();
    }

    private void OnVerTodosClick(object sender, RoutedEventArgs e)
    {
        ExitFocusMode(animated: true);
    }

    /// <summary>
    /// Anima a volta à visão "ver todos": escala para caber todos os cards.
    /// </summary>
    private void AnimateVerTodos()
    {
        if (_mainVm == null || _mainVm.Terminals.Count == 0)
        {
            CenterCanvas();
            return;
        }

        double viewW = CanvasArea.ActualWidth;
        double viewH = CanvasArea.ActualHeight;

        // Calcular bounding box de todos os cards
        double totalW = TerminalsPanel.ActualWidth;
        double totalH = TerminalsPanel.ActualHeight;

        if (totalW <= 0 || totalH <= 0)
        {
            // Fallback: escala 1:1
            AnimateCanvasTo(1.0, CardPadding, CardPadding);
            return;
        }

        double scaleX = (viewW * 0.92) / totalW;
        double scaleY = (viewH * 0.92) / totalH;
        double scale  = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxZoom);

        double transX = (viewW - totalW * scale) / 2;
        double transY = (viewH - totalH * scale) / 2;

        AnimateCanvasTo(scale, transX, transY);
    }

    /// <summary>
    /// Centraliza o canvas na visão atual sem animação.
    /// </summary>
    private void CenterCanvas()
    {
        CanvasTranslate.X = CardPadding;
        CanvasTranslate.Y = CardPadding;
        CanvasScale.ScaleX = 1.0;
        CanvasScale.ScaleY = 1.0;
        UpdateZoomLabel(1.0);
    }

    /// <summary>
    /// Anima ScaleTransform e TranslateTransform para os valores alvo.
    /// </summary>
    private void AnimateCanvasTo(double targetScale, double targetTransX, double targetTransY)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(280));
        var ease     = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // Usar FillBehavior=Stop para que os valores fiquem livres após a animação.
        // Ao terminar, atribuímos os valores finais manualmente.
        double finalScale  = targetScale;
        double finalTransX = targetTransX;
        double finalTransY = targetTransY;

        // Animar Scale
        var scaleAnim = new DoubleAnimation(targetScale, duration)
        {
            EasingFunction = ease,
            FillBehavior   = FillBehavior.Stop
        };
        scaleAnim.Completed += (s, e) =>
        {
            CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            CanvasScale.ScaleX = finalScale;
            CanvasScale.ScaleY = finalScale;
        };
        CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, duration)
        {
            EasingFunction = ease,
            FillBehavior   = FillBehavior.Stop
        });

        // Animar TranslateX
        var transXAnim = new DoubleAnimation(targetTransX, duration)
        {
            EasingFunction = ease,
            FillBehavior   = FillBehavior.Stop
        };
        transXAnim.Completed += (s, e) =>
        {
            CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            CanvasTranslate.X = finalTransX;
            CanvasTranslate.Y = finalTransY;
        };
        CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, transXAnim);

        // Animar TranslateY
        var transYAnim = new DoubleAnimation(targetTransY, duration)
        {
            EasingFunction = ease,
            FillBehavior   = FillBehavior.Stop
        };
        CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, transYAnim);

        // Atualizar label de zoom imediatamente (valor final)
        UpdateZoomLabel(targetScale);
    }

    // ─── Resolvição tardia do MainViewModel ──────────────────────────────────

    /// <summary>
    /// Chamado pelo Loaded — garante que o ViewModel foi resolvido via Window.
    /// </summary>
    private void ResolveOnLoad()
    {
        if (_mainVm == null)
            ResolveMainViewModel();
    }
}
