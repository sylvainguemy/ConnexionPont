# ConnexionPont V0.2

Depuis votre PC, accédez au PC de pont chez le client en RDP, à un serveur interne en SSH et à deux applications web sur les ports 8080 / 8090. Le PC de pont établit une connexion sortante TLS vers votre IP publique fixe.

| Machine | Fenêtre | EXE (nom technique conservé) |
| --- | --- | --- |
| Votre PC, chez vous | Accès chez vous | ConnexionPont.Relais.exe |
| PC de pont, chez le client | Pont chez le client | ConnexionPont.Agent.exe |

## Installation et lancement portable

Lire [INSTALLATION.md](INSTALLATION.md), notamment la section **PC de pont sans installation ni droits administrateur**. Le ZIP contient les deux EXE et leurs `.config`, les scripts facultatifs et le guide.

.NET Framework 4.8 doit déjà être présent. L'agent fonctionne sans installation. RDP doit être déjà activé et autorisé sur le pont si vous souhaitez son bureau. Le diagnostic SSH et web ne nécessite pas d'activer RDP.

## Accès depuis chez vous

```powershell
mstsc /v:127.0.0.1:13389
ssh -p 10022 utilisateur@127.0.0.1
```

Pour le web : renseigner **Serveur web interne** dans la fenêtre Accès, enregistrer, exporter le profil puis l'importer sur le pont. Cliquer **Web 8080** ou **Web 8090** ouvre Edge / Chrome dans un profil dédié au tunnel. Les URL et les en-têtes Host d'origine sont conservés. Vous pouvez ensuite saisir ces mêmes URL dans cette fenêtre.

Le proxy SOCKS5 écoute uniquement sur `127.0.0.1:1080` et n'autorise que le serveur web du profil sur les ports 8080 et 8090. Aucun réglage proxy global ni nouvelle redirection Freebox n'est nécessaire. La fenêtre dédiée sert à ces applications internes ; le navigateur habituel conserve ses paramètres.

## Mise à jour de V0.1

Mettre à jour **les deux EXE et leurs .config**, arrêter puis relancer les applications. Les configurations et certificats sont conservés. Renseigner le serveur web sur le poste Accès, puis exporter/réimporter le profil sur le pont. V0.2 utilise le protocole RDT2 ; les deux côtés doivent être à jour.

## Compilation et tests

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

La compilation utilise le compilateur C# de Windows, sans package NuGet. Le ZIP est produit dans `dist\ConnexionPont-Windows-v0.2.0.zip` après les tests. Le workflow GitHub Windows vérifie TLS, les routes RDP/SSH/web, les hôtes autorisés, la reprise et, si présent, un navigateur Chromium réel en mode headless. Voir [docs/VALIDATION.md](docs/VALIDATION.md).

## Protection et limites

TLS 1.2 / Schannel, certificats RSA 3072 appairés et vérifiés par empreinte SHA-256 ; stockage local DPAPI pour le compte Windows courant. Le profil portable est protégé par PBKDF2-SHA256, AES-256-CBC et HMAC-SHA256. Aucun mot de passe RDP/SSH n'est enregistré. Le certificat privé du poste Accès n'est jamais exporté vers le pont.

Les accès RDP, SSH et proxy web écoutent uniquement en boucle locale ; 32 flux de données simultanés maximum. Un seul pont appairé connecté à la fois. Les fenêtres doivent rester ouvertes et les sessions Windows actives. Fermer une fenêtre coupe le tunnel ; les connexions sont réessayées automatiquement après une panne réseau.

Le transport sortant du pont est du TLS direct sur TCP, sans HTTP CONNECT : le réseau du client doit l'autoriser. La V0.2 reste à qualifier sur votre Freebox et les applications réelles ; elle n'est pas un service Windows, les EXE ne sont pas signés Authenticode et aucun audit indépendant n'a été réalisé. Voir [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
