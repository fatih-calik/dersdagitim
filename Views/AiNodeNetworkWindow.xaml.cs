using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using DersDagitim.Services;
using Microsoft.Web.WebView2.Core;

namespace DersDagitim.Views
{
    public partial class AiNodeNetworkWindow : Window
    {
        private DependencyAnalysisService _analysisService;

        public AiNodeNetworkWindow()
        {
            InitializeComponent();
            _analysisService = new DependencyAnalysisService();
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                await wvMain.EnsureCoreWebView2Async();
                wvMain.NavigationCompleted += (s, e) => {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                };
                LoadGraph();
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2 başlatılamadı: " + ex.Message);
            }
        }

        private void LoadGraph()
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            var data = _analysisService.GetAnalysisData();

            TxtStrain.Text = data.overallStrain > 75 ? "KRİTİK" : (data.overallStrain > 50 ? "YÜKSEK" : "NORMAL");
            TxtStrain.Foreground = data.overallStrain > 75 ? System.Windows.Media.Brushes.Red :
                                   (data.overallStrain > 50 ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.LightGreen);

            string jsonData = JsonSerializer.Serialize(data);
            string htmlTemplate = GetHtmlTemplate(jsonData);

            wvMain.NavigateToString(htmlTemplate);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            LoadGraph();
        }

        private string GetHtmlTemplate(string jsonData)
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>AI Node Network</title>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/cytoscape/3.26.0/cytoscape.min.js'></script>
    <style>
        body { margin: 0; padding: 0; overflow: hidden; background-color: #0f172a; font-family: 'Segoe UI', system-ui, sans-serif; }
        #cy { width: 100vw; height: 100vh; display: block; }
        
        /* Stats Panel */
        .info-panel {
            position: absolute; bottom: 30px; right: 30px;
            background: rgba(15, 23, 42, 0.95);
            color: #f8fafc; padding: 20px; border-radius: 16px;
            border: 1px solid #334155; pointer-events: none;
            width: 280px; backdrop-filter: blur(12px);
            box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5);
            display: none; transition: all 0.3s ease;
        }
        .info-header { font-size: 18px; font-weight: 700; margin-bottom: 12px; border-bottom: 1px solid #334155; padding-bottom: 8px; color: #38bdf8; }
        .stat-item { display: flex; justify-content: space-between; margin-bottom: 8px; font-size: 13px; }
        .stat-label { color: #94a3b8; }
        .stat-value { font-weight: 600; color: #f1f5f9; }
        .status-pill { padding: 2px 8px; border-radius: 999px; font-size: 11px; font-weight: 700; text-transform: uppercase; }

        /* Context Menu */
        #cmenu {
            position: absolute; display: none; z-index: 1000;
            background: #1e293b; border: 1px solid #475569;
            border-radius: 8px; padding: 4px; min-width: 220px;
            box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.3);
        }
        .menu-item {
            padding: 8px 12px; color: #e2e8f0; font-size: 13px;
            cursor: pointer; border-radius: 4px; transition: background 0.2s;
        }
        .menu-item:hover { background: #334155; color: #38bdf8; }
        .menu-divider { height: 1px; background: #334155; margin: 4px 0; }
        .menu-header { padding: 4px 12px; font-size: 11px; color: #64748b; font-weight: 600; text-transform: uppercase; }
    </style>
</head>
<body>
    <div id='cy'></div>
    <div id='info' class='info-panel'></div>
    
    <div id='cmenu'>
        <div class='menu-header' id='menu-title'>Öğretmen Seçenekleri</div>
        <div class='menu-item' onclick='filterEdges(""all"")'>Bağlantıların Tümünü Göster</div>
        <div class='menu-divider'></div>
        <div class='menu-item' onclick='filterEdges(""team"")'>Aynı Derse (Bloka) Giren Öğretmenler</div>
        <div class='menu-item' onclick='filterEdges(""class"")'>Aynı Sınıfa Giren Öğretmenler</div>
        <div class='menu-item' onclick='filterEdges(""lesson"")'>Kardeş Ders Grubunda Olanlar</div>
        <div class='menu-item' onclick='filterEdges(""room"")'>Aynı Mekanı Kullanan Öğretmenler</div>
    </div>

    <script>
        const data = " + jsonData + @";
        let currentNode = null;

        const cy = cytoscape({
            container: document.getElementById('cy'),
            elements: [
                ...data.nodes.map(n => ({ data: { ...n } })),
                ...data.edges.map(e => ({ data: { ...e } }))
            ],
            style: [
                {
                    selector: 'node',
                    style: {
                        'background-color': 'data(color)',
                        'label': 'data(label)',
                        'color': '#fff',
                        'font-size': '11px',
                        'text-valign': 'center',
                        'text-halign': 'center',
                        'width': 'data(size)',
                        'height': 'data(size)',
                        'border-width': 1.5,
                        'border-color': '#fff',
                        'text-outline-color': '#000',
                        'text-outline-width': 1,
                        'font-weight': 'bold',
                        'overlay-padding': '6px',
                        'z-index': 10
                    }
                },
                {
                    selector: 'edge',
                    style: {
                        'width': 'mapData(weight, 0, 15, 2, 12)',
                        'line-color': 'data(color)',
                        'curve-style': 'bezier',
                        'opacity': 0.5,
                        'arrow-scale': 1.2
                    }
                },
                {
                    selector: '.faded',
                    style: { 'opacity': 0.1, 'text-opacity': 0 }
                },
                {
                    selector: '.highlight',
                    style: { 'opacity': 1, 'text-opacity': 1, 'z-index': 999 }
                },
                {
                    selector: ':selected',
                    style: {
                        'border-width': 4,
                        'border-color': '#38bdf8',
                        'overlay-opacity': 0.2
                    }
                }
            ],
            layout: {
                name: 'cose',
                animate: true,
                refresh: 20,
                fit: true,
                padding: 100,
                nodeRepulsion: 10000000,
                idealEdgeLength: 200,
                edgeElasticity: 100,
                nestingFactor: 5,
                gravity: 80,
                numIter: 1000,
                initialTemp: 200,
                coolingFactor: 0.95,
                minTemp: 1.0,
                nodeOverlap: 20,
                componentSpacing: 100
            }
        });

        // Context Menu Logic
        cy.on('cxttap', 'node', function(evt){
            currentNode = evt.target;
            const menu = document.getElementById('cmenu');
            document.getElementById('menu-title').innerText = currentNode.data('label');
            menu.style.display = 'block';
            menu.style.left = evt.renderedPosition.x + 'px';
            menu.style.top = evt.renderedPosition.y + 'px';
        });

        window.onclick = () => { document.getElementById('cmenu').style.display = 'none'; };

        function filterEdges(type) {
            if(!currentNode) return;
            
            cy.elements().addClass('faded').removeClass('highlight');
            currentNode.removeClass('faded').addClass('highlight');
            
            const connectedEdges = currentNode.connectedEdges();
            connectedEdges.forEach(edge => {
                if (type === 'all' || edge.data('type').includes(type)) {
                    edge.removeClass('faded').addClass('highlight');
                    edge.connectedNodes().removeClass('faded').addClass('highlight');
                }
            });
        }

        // Info Panel Update
        cy.on('select tap', 'node', function(evt){
            const n = evt.target.data();
            const info = document.getElementById('info');
            
            let statusColor = '#22c55e';
            let statusText = 'Düşük Risk';
            if(n.stress > 75) { statusColor = '#ef4444'; statusText = 'Kritik'; }
            else if(n.stress > 50) { statusColor = '#f97316'; statusText = 'Yoğun'; }
            
            info.innerHTML = `
                <div class='info-header'>${n.label}</div>
                <div class='stat-item'>
                    <span class='stat-label'>Bağımlılık Durumu:</span>
                    <span class='status-pill' style='background: ${statusColor}44; color: ${statusColor}; border: 1px solid ${statusColor}'>${statusText}</span>
                </div>
                <div class='stat-item'>
                    <span class='stat-label'>Stres Skoru:</span>
                    <span class='stat-value'>%${Math.round(n.stress)}</span>
                </div>
                <div class='stat-item'>
                    <span class='stat-label'>Toplam Ders Saati:</span>
                    <span class='stat-value'>${n.lessonCount} Saat</span>
                </div>
                <div class='stat-item'>
                    <span class='stat-label'>Girdiği Sınıf Sayısı:</span>
                    <span class='stat-value'>${n.classCount} Sınıf</span>
                </div>
                <div class='stat-item'>
                    <span class='stat-label'>Ortak Mekan Kaydı:</span>
                    <span class='stat-value'>${n.roomCount} Mekan</span>
                </div>
                <div class='stat-item'>
                    <span class='stat-label'>Bağımlı Olduğu Kişi:</span>
                    <span class='stat-value'>${n.relationCount} Kişi</span>
                </div>
                <div style='font-size: 11px; color: #64748b; margin-top: 10px; font-style: italic;'>
                    * Bağlantıları filtrelemek için öğretmene sağ tıklayın.
                </div>
            `;
            info.style.display = 'block';
        });

        cy.on('tap', function(evt){
            if(evt.target === cy){
                cy.elements().removeClass('faded highlight');
                document.getElementById('info').style.display = 'none';
            }
        });

        // Edge Hover
        cy.on('mouseover', 'edge', function(evt){
            const e = evt.target.data();
            if(evt.target.hasClass('faded')) return;
            const info = document.getElementById('info');
            info.innerHTML = `
                <div class='info-header' style='color: #fb7185'>İlişki Detayı</div>
                <div class='stat-item'>
                    <span class='stat-label'>Sebep(ler):</span>
                    <span class='stat-value'>${e.label}</span>
                </div>
                <div class='stat-item'>
                    <span class='stat-label'>İlişki Gücü:</span>
                    <span class='stat-value'>${e.weight.toFixed(1)}</span>
                </div>
            `;
            info.style.display = 'block';
        });

    </script>
</body>
</html>";
        }
    }
}
