# SteamFreeLicensePurger

Ein Tool zum automatischen Entfernen aller kostenlosen (FreeOnDemand) Paket-Lizenzen von einem Steam-Account. Es ignoriert gekaufte Spiele und entfernt ausschließlich Lizenzen, die keine eigenständigen Kosten verursacht haben und jederzeit neu beschafft werden können.

*Irgendwann im Leben kommst du auf die Idee, mit dem https://steamdb.info/freepackages/ Tool einfach alle Gratis Pakete, die möglich sind, hinzuzufügen. Es ist wichtig, dass du es nicht tust. Solltest du es doch getan haben, ist dieses Tool für dich da*

## Neue Funktionen in diesem Fork

* **Lokale Session-Speicherung:** Speichert Zugangsdaten und das Refresh-Token in einer lokalen `credentials.json`. Nach dem ersten Login entfällt die erneute Eingabe von Passwort und Steam-Guard-Code.
* **Automatischer Start (Timeout):** Die Bestätigungsabfrage vor dem Löschen startet nach 60 Sekunden ohne Eingabe automatisch ohne das ein `yes` eingegeben werden muss.
* **Blacklist für fehlerhafte AppIDs:** Lizenzen, die von Steam serverseitig blockiert werden (Fehlercode `InvalidParam`), werden automatisch in eine `blacklist.json` geschrieben und bei zukünftigen Durchläufen sofort übersprungen.
* **Auto-Reconnect bei Rate-Limits:** Wenn Steam die Verbindung wegen zu vieler Anfragen trennt, pausiert das Skript für 30 Minuten und setzt den Vorgang danach selbstständig fort, ohne zu crashen. Das Programm kann dadurch komplett unbeaufsichtigt laufen.

## Voraussetzungen

* .NET SDK

## Benutzung

1. Das Repository klonen oder herunterladen.
2. Ein Terminal im entsprechenden Ordner öffnen.
3. Den Befehl `dotnet run` ausführen.
4. Beim ersten Start Steam-Benutzernamen, Passwort und Steam-Guard-Code eingeben.

Das Tool erstellt nach dem ersten Login alle nötigen Konfigurationsdateien selbstständig.

## Schutzmechanismen

Das Skript fragt direkt bei Steam die Paketinformationen (PICS) ab. 
Es werden ausschließlich Pakete gelöscht, die den Status `Available`, den Lizenztyp `SinglePurchase` und den Abrechnungstyp `FreeOnDemand` besitzen. 
Jede AppID, die an eine bezahlte Lizenz geknüpft ist, wird übersprungen und nicht angetastet.

## Funktionsweise

1. Meldet sich beim Steam-Konto an (Benutzername + Passswort, direkt über die Steam Console, ggf. muss dies mit den Authentikator bestätigt werden, Häkchen für Maschine merken nicht nötig)
2. Ruft alle Lizenzen ab und identifiziert kostenlose (gratis) Lizenzen, von F2P Games oder Demos
3. Überprüft die Paketinformationen, um sicherzustellen, dass sie auch (immer noch) „FreeOnDemand“ (jederzeit hinzufügbar) und „Available“ (also verfügbar, die Buttons sind noch da, die Appseite gibt es noch, etc) sind
4. Schützt Anwendungen, die durch kostenpflichtige Lizenzen abgedeckt sind (also auch der Gratis DLC zu einem deiner Games)
5. Listet alles auf, was es findet und entfernt die kostenlosen Lizenzen nach Bestätigung (bzw. 60 Sekunden Timer)

## Ausgabe

- Die Konsole zeigt den Fortschritt in Echtzeit an
- Es wird eine Protokolldatei `RemovedLicenses_{AccountID}.log` mit allen Aktionen erstellt, sowie ein blacklist.json, mit allen IDs, deren Entfernung zu Fehlern führen 
- Änderungen an der Lizenzliste werden direkt bearbeitet und angezeigt

## Haftungsausschluss

**VERWENDUNG AUF EIGENE GEFAHR**

Dieses Tool wird ohne Mängelgewähr und ohne Support bereitgestellt. Wenn du etwas entfernst, das:
- eigentlich nicht kostenlos war
- nicht erneut bezogen werden kann
- eine zeitlich begrenzte Aktion war
ist es deine eigene Schuld und nicht die, meines Programs, von mir oder irgend einen anderen Wesen. Du bist für dein Handeln selbst verantwortlich.

Es steht kein Support zur Verfügung. Bei der Verwendung tragt ihr die alleinige Verantwortung für alle aus dem eigenen Konto entfernten Lizenzen.

## Verwendung

[.NET](https://dotnet.microsoft.com/download) installieren und Folgendes ausführen:

```
dotnet run
```
