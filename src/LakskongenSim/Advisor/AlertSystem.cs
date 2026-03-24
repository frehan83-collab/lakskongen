using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Advisor
{
    /// <summary>
    /// Alert system for real-time notifications about critical conditions,
    /// threshold breaches, and market opportunities.
    /// </summary>
    public class AlertSystem
    {
        private readonly List<Alert> _activeAlerts = new();
        private readonly List<Alert> _alertHistory = new();
        private readonly Dictionary<string, DateTime> _suppressedAlerts = new();
        private int _alertCounter = 0;

        /// <summary>Event fired when a new alert is generated.</summary>
        public event Action<Alert>? OnAlertGenerated;

        /// <summary>Event fired when an alert is resolved.</summary>
        public event Action<Alert>? OnAlertResolved;

        /// <summary>Currently active (unresolved) alerts.</summary>
        public IReadOnlyList<Alert> ActiveAlerts => _activeAlerts.AsReadOnly();

        /// <summary>All alerts in history.</summary>
        public IReadOnlyList<Alert> AlertHistory => _alertHistory.AsReadOnly();

        /// <summary>Maximum number of active alerts before auto-cleanup.</summary>
        public int MaxActiveAlerts { get; set; } = 50;

        /// <summary>How long to suppress similar alerts (prevents spam).</summary>
        public TimeSpan SuppressionDuration { get; set; } = TimeSpan.FromHours(6);

        // ─── Alert Generation ────────────────────────────────────────────────

        /// <summary>Create a new alert if not suppressed.</summary>
        public Alert? CreateAlert(
            AlertType type,
            AlertSeverity severity,
            string title,
            string message,
            string? zoneId = null,
            string? siteId = null,
            double? thresholdValue = null,
            double? currentValue = null)
        {
            // Check suppression
            var suppressionKey = $"{type}:{zoneId}:{siteId}";
            if (_suppressedAlerts.TryGetValue(suppressionKey, out var suppressedUntil))
            {
                if (DateTime.UtcNow < suppressedUntil)
                {
                    return null; // Alert is suppressed
                }
                _suppressedAlerts.Remove(suppressionKey);
            }

            var alert = new Alert
            {
                Id = $"ALT-{++_alertCounter:0000}",
                CreatedAt = DateTime.UtcNow,
                Type = type,
                Severity = severity,
                Title = title,
                Message = message,
                ZoneId = zoneId,
                SiteId = siteId,
                ThresholdValue = thresholdValue,
                CurrentValue = currentValue,
                IsRead = false,
                IsResolved = false
            };

            _activeAlerts.Add(alert);
            _alertHistory.Add(alert);

            // Apply suppression
            _suppressedAlerts[suppressionKey] = DateTime.UtcNow + SuppressionDuration;

            // Auto-cleanup if too many active alerts
            CleanupOldAlerts();

            OnAlertGenerated?.Invoke(alert);
            return alert;
        }

        /// <summary>Resolve an alert.</summary>
        public void ResolveAlert(string alertId, string? resolution = null)
        {
            var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            if (alert != null)
            {
                alert.IsResolved = true;
                alert.ResolvedAt = DateTime.UtcNow;
                alert.Resolution = resolution;
                _activeAlerts.Remove(alert);
                OnAlertResolved?.Invoke(alert);
            }
        }

        /// <summary>Resolve all alerts of a specific type.</summary>
        public int ResolveAlertsByType(AlertType type, string? resolution = null)
        {
            var toResolve = _activeAlerts.Where(a => a.Type == type).ToList();
            foreach (var alert in toResolve)
            {
                alert.IsResolved = true;
                alert.ResolvedAt = DateTime.UtcNow;
                alert.Resolution = resolution;
                OnAlertResolved?.Invoke(alert);
            }
            _activeAlerts.RemoveAll(a => a.Type == type);
            return toResolve.Count;
        }

        /// <summary>Mark an alert as read.</summary>
        public void MarkAsRead(string alertId)
        {
            var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            if (alert != null)
            {
                alert.IsRead = true;
            }
        }

        /// <summary>Mark all alerts as read.</summary>
        public void MarkAllAsRead()
        {
            foreach (var alert in _activeAlerts)
            {
                alert.IsRead = true;
            }
        }

        /// <summary>Dismiss an alert without resolving.</summary>
        public void DismissAlert(string alertId)
        {
            var alert = _activeAlerts.FirstOrDefault(a => a.Id == alertId);
            if (alert != null)
            {
                alert.IsResolved = true;
                alert.ResolvedAt = DateTime.UtcNow;
                alert.Resolution = "Dismissed by user";
                _activeAlerts.Remove(alert);
                OnAlertResolved?.Invoke(alert);
            }
        }

        // ─── Convenience Methods ─────────────────────────────────────────────

        /// <summary>Create an environmental alert.</summary>
        public Alert? EnvironmentalAlert(
            AlertSeverity severity,
            string title,
            string message,
            string zoneId,
            double thresholdValue,
            double currentValue)
        {
            return CreateAlert(
                AlertType.Environmental,
                severity,
                title,
                message,
                zoneId,
                null,
                thresholdValue,
                currentValue);
        }

        /// <summary>Create a biological/health alert.</summary>
        public Alert? HealthAlert(
            AlertSeverity severity,
            string title,
            string message,
            string? siteId = null,
            string? zoneId = null)
        {
            return CreateAlert(
                AlertType.Biological,
                severity,
                title,
                message,
                zoneId,
                siteId);
        }

        /// <summary>Create a market opportunity alert.</summary>
        public Alert? MarketAlert(
            AlertSeverity severity,
            string title,
            string message,
            double? priceTarget = null,
            double? currentPrice = null)
        {
            return CreateAlert(
                AlertType.MarketOpportunity,
                severity,
                title,
                message,
                null,
                null,
                priceTarget,
                currentPrice);
        }

        /// <summary>Create a financial/cash flow alert.</summary>
        public Alert? FinancialAlert(
            AlertSeverity severity,
            string title,
            string message,
            double? thresholdValue = null,
            double? currentValue = null)
        {
            return CreateAlert(
                AlertType.Financial,
                severity,
                title,
                message,
                null,
                null,
                thresholdValue,
                currentValue);
        }

        /// <summary>Create an operational alert.</summary>
        public Alert? OperationalAlert(
            AlertSeverity severity,
            string title,
            string message,
            string? siteId = null)
        {
            return CreateAlert(
                AlertType.Operational,
                severity,
                title,
                message,
                null,
                siteId);
        }

        /// <summary>Create a regulatory/compliance alert.</summary>
        public Alert? ComplianceAlert(
            AlertSeverity severity,
            string title,
            string message)
        {
            return CreateAlert(
                AlertType.Regulatory,
                severity,
                title,
                message);
        }

        // ─── Query Methods ───────────────────────────────────────────────────

        /// <summary>Get critical alerts (severity = Critical).</summary>
        public IEnumerable<Alert> GetCriticalAlerts()
        {
            return _activeAlerts.Where(a => a.Severity == AlertSeverity.Critical);
        }

        /// <summary>Get unread alerts.</summary>
        public IEnumerable<Alert> GetUnreadAlerts()
        {
            return _activeAlerts.Where(a => !a.IsRead);
        }

        /// <summary>Get alerts for a specific zone.</summary>
        public IEnumerable<Alert> GetAlertsForZone(string zoneId)
        {
            return _activeAlerts.Where(a => a.ZoneId == zoneId);
        }

        /// <summary>Get alerts for a specific site.</summary>
        public IEnumerable<Alert> GetAlertsForSite(string siteId)
        {
            return _activeAlerts.Where(a => a.SiteId == siteId);
        }

        /// <summary>Check if there's an active critical alert.</summary>
        public bool HasCriticalAlert()
        {
            return _activeAlerts.Any(a => a.Severity == AlertSeverity.Critical);
        }

        /// <summary>Get count of alerts by severity.</summary>
        public Dictionary<AlertSeverity, int> GetAlertCounts()
        {
            return _activeAlerts.GroupBy(a => a.Severity)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>Clear all suppression timers.</summary>
        public void ClearSuppressions()
        {
            _suppressedAlerts.Clear();
        }

        // ─── Private Helpers ─────────────────────────────────────────────────

        private void CleanupOldAlerts()
        {
            // Remove oldest non-critical alerts if over limit
            while (_activeAlerts.Count > MaxActiveAlerts)
            {
                var oldestNonCritical = _activeAlerts
                    .Where(a => a.Severity != AlertSeverity.Critical)
                    .OrderBy(a => a.CreatedAt)
                    .FirstOrDefault();
                
                if (oldestNonCritical != null)
                {
                    oldestNonCritical.IsResolved = true;
                    oldestNonCritical.ResolvedAt = DateTime.UtcNow;
                    oldestNonCritical.Resolution = "Auto-resolved: exceeded alert limit";
                    _activeAlerts.Remove(oldestNonCritical);
                }
                else
                {
                    // All alerts are critical, remove oldest
                    var oldest = _activeAlerts.OrderBy(a => a.CreatedAt).First();
                    oldest.IsResolved = true;
                    oldest.ResolvedAt = DateTime.UtcNow;
                    oldest.Resolution = "Auto-resolved: exceeded alert limit";
                    _activeAlerts.Remove(oldest);
                }
            }

            // Cleanup old suppression entries
            var expired = _suppressedAlerts
                .Where(kvp => kvp.Value < DateTime.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in expired)
            {
                _suppressedAlerts.Remove(key);
            }
        }
    }

    /// <summary>Represents a single alert/notification.</summary>
    public class Alert
    {
        public string Id { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public AlertType Type { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? ZoneId { get; set; }
        public string? SiteId { get; set; }
        public double? ThresholdValue { get; set; }
        public double? CurrentValue { get; set; }
        public bool IsRead { get; set; }
        public bool IsResolved { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? Resolution { get; set; }

        /// <summary>Gets emoji indicator based on severity.</summary>
        public string SeverityEmoji => Severity switch
        {
            AlertSeverity.Critical => "🔴",
            AlertSeverity.Warning => "🟠",
            AlertSeverity.Advisory => "🟡",
            AlertSeverity.Info => "🔵",
            AlertSeverity.Opportunity => "🟢",
            _ => "⚪"
        };

        /// <summary>Gets formatted severity label in Norwegian.</summary>
        public string SeverityLabel => Severity switch
        {
            AlertSeverity.Critical => "Kritisk",
            AlertSeverity.Warning => "Advarsel",
            AlertSeverity.Advisory => "Råd",
            AlertSeverity.Info => "Info",
            AlertSeverity.Opportunity => "Mulighet",
            _ => "Ukjent"
        };
    }

    /// <summary>Types of alerts.</summary>
    public enum AlertType
    {
        Environmental,
        Biological,
        MarketOpportunity,
        Financial,
        Operational,
        Regulatory,
        System
    }

    /// <summary>Alert severity levels.</summary>
    public enum AlertSeverity
    {
        Info = 0,
        Advisory = 1,
        Opportunity = 2,
        Warning = 3,
        Critical = 4
    }
}
