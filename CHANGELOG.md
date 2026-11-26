# Versions

## 0.2.0 — 2026-11-26

- Accès web via le pont, avec URL d'origine conservée sur les ports 8080 et 8090.
- Proxy SOCKS5 local limité aux deux destinations du profil et boutons Edge / Chrome avec profil dédié.
- Noms de fenêtres « Accès chez vous » / « Pont chez le client », rôles d'installation Acces / Pont avec compatibilité Relais / Agent.
- Procédure portable sans installation ni droits administrateur sur le pont.
- Commandes PowerShell corrigées pour les politiques d'exécution de scripts.
- Protocole RDT2 et 32 flux simultanés ; mise à jour des deux EXE requise.
- Tests HTTP, cookies, filtrage des destinations et navigateur Chromium réel quand installé.

## 0.1.0 — 2026-11-26

- Relais Windows et agent Windows avec interface française.
- Tunnel inverse TCP / TLS 1.2 avec authentification mutuelle et appairage de certificats.
- Accès RDP au PC agent et accès SSH direct à un serveur interne, depuis un terminal local.
- Profils agent chiffrés et configuration locale DPAPI.
- Reconnexion automatique, diagnostic TCP, boutons RDP / SSH.
- Scripts d'installation utilisateur, pare-feu relais et désinstallation ; guide Freebox.
- Tests automatisés et compilation Windows. Version initiale à qualifier sur le réseau réel.
