# ConnexionPont

Application Windows permettant au PC d'un client d'établir un tunnel inverse vers votre PC, derrière une Freebox avec IP publique fixe. Depuis chez vous, utilisez le Bureau à distance vers le PC client et SSH vers un serveur interne joignable par ce PC.

| Programme | Où le lancer | Fonction |
| --- | --- | --- |
| `ConnexionPont.Relais.exe` | Votre PC, chez vous | Écoute du tunnel et accès locaux RDP / SSH |
| `ConnexionPont.Agent.exe` | PC Windows chez le client | Connexion sortante vers votre IP et accès aux deux destinations autorisées |

Les destinations sont le RDP du PC agent (`127.0.0.1:3389`) et le serveur SSH renseigné dans le profil (port `22`). Le relais ne propose pas de proxy général ni de commandes à distance.

## Installation

Lire [INSTALLATION.md](INSTALLATION.md). La livraison Windows contient les deux EXE, leurs fichiers `.config`, les scripts PowerShell et le guide. .NET Framework 4.8 est requis ; aucun package NuGet, Chisel, VPN ou serveur cloud n'est nécessaire.

Les exécutables sont des applications visibles. Le tunnel s'arrête à la fermeture de la fenêtre ou à la fermeture de session Windows. L'agent essaie de se reconnecter si la connexion est interrompue. La V0.1 n'est pas un service Windows.

## Depuis votre terminal

Après connexion de l'agent :

```powershell
# RDP vers le PC du client
mstsc /v:127.0.0.1:13389

# SSH vers le serveur interne (son port reste 22)
ssh -p 10022 utilisateur@127.0.0.1

# Avec une clé SSH conservée sur votre PC
ssh -i C:\Users\vous\.ssh\id_ed25519 -p 10022 utilisateur@127.0.0.1
```

Le bouton **Copier la commande SSH** ajoute `HostKeyAlias` avec l'identifiant d'appairage afin de distinguer les clés SSH lorsque plusieurs profils sont utilisés successivement. Vérifiez toujours l'empreinte SSH du serveur lors de la première connexion.

## Compilation et tests

Depuis Windows 64 bits avec .NET Framework 4.8 installé :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Le compilateur C# Windows intégré est utilisé ; Visual Studio et un SDK .NET récent ne sont pas nécessaires. Le script exécute les tests avant de produire `dist\ConnexionPont-Windows-v0.1.0.zip`. Les tests réseau requièrent des connexions TCP locales autorisées. Un workflow GitHub Windows lance la même compilation et les tests.

## Protection des accès

- TLS 1.2 par `SslStream` / Schannel, certificats RSA 3072 et authentification mutuelle ; chaque côté vérifie l'empreinte SHA-256 exacte du certificat distant et sa période de validité.
- Les ports RDP et SSH du relais écoutent exclusivement sur `127.0.0.1`.
- Le profil portable agent est chiffré : PBKDF2-SHA256 (200 000 itérations), AES-256-CBC et HMAC-SHA256, vérifié avant déchiffrement. Mot de passe d'au moins 12 caractères.
- Les configurations enregistrées localement sont chiffrées par DPAPI pour le compte Windows courant. Aucun certificat racine n'est installé et aucune clé privée relais n'est exportée vers l'agent.
- Le tunnel conserve l'authentification native RDP / SSH. Aucun mot de passe Windows ou SSH n'est enregistré par ConnexionPont.
- Une seule identité agent connectée à la fois ; huit sessions de données simultanées au maximum. Les certificats expirent après deux ans ; réappairer avant l'expiration.

## Limites de cette version

La V0.1 est une première version à qualifier sur votre Freebox et le réseau du client. Les tests automatisés utilisent deux destinations TCP de test et vérifient le transport : ils ne remplacent pas une connexion réelle à `mstsc` ou à un serveur SSH d'entreprise. Pas d'audit de sécurité indépendant, signature Authenticode, MSI, service Windows, administration multi-clients ou bascule automatique vers un relais cloud.

Le transport est du **TLS direct sur TCP**, et non un protocole HTTPS/WebSocket. Un proxy HTTP obligatoire, une inspection TLS, une politique de sortie ou un antivirus peuvent empêcher son fonctionnement. Le port 443 seul ne garantit pas l'acceptation de la connexion ; faire autoriser le trafic adapté. La validation des certificats n'est jamais désactivée.

L'installation et la mise en réseau s'effectuent dans le cadre des accès autorisés par le client.

Voir également [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) et [docs/VALIDATION.md](docs/VALIDATION.md).
