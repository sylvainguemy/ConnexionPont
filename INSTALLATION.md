# Installation — ConnexionPont Windows V0.1

## 1. Prérequis et ports

Votre PC et le PC client : Windows 10/11 64 bits, .NET Framework 4.8 installé, comptes Windows habituels. Le PC client doit utiliser une édition Pro, Entreprise ou Éducation pour recevoir du RDP natif. L'accès SSH fonctionne indépendamment de cette édition.

| Emplacement | Port par défaut | Destination / rôle |
| --- | --- | --- |
| Freebox, côté Internet | TCP 443 | Redirection vers le port 8443 du PC relais |
| Votre PC relais | TCP 8443 | Écoute TLS de ConnexionPont |
| Votre PC, boucle locale seulement | TCP 13389 | RDP du PC client, port 3389 |
| Votre PC, boucle locale seulement | TCP 10022 | SSH du serveur interne, port 22 |
| PC client, boucle locale | TCP 3389 | Service Bureau à distance Windows |
| Serveur interne, depuis le PC client | TCP 22 | Service SSH existant |

Le port **10022 n'est jamais à ouvrir sur le serveur interne**. Il n'existe que sur votre PC. Les ports 3389, 13389 et 10022 ne sont pas redirigés sur la Freebox.

## 2. Installer les fichiers

Extraire complètement `ConnexionPont-Windows-v0.1.0.zip`. Ne pas exécuter directement depuis le ZIP. Conserver les fichiers `.exe.config` à côté des EXE.

Option portable : lancer l'EXE correspondant directement depuis le dossier extrait.

Option installation pour votre compte Windows, depuis le dossier extrait :

```powershell
# Chez vous
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Installer.ps1 -Role Relais

# Sur le PC du client
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Installer.ps1 -Role Agent
```

Le script copie l'application dans `%LOCALAPPDATA%\ConnexionPont\Applications\Relais` ou `Agent`, crée un raccourci sur le Bureau et dans le menu Démarrer. Aucun droit administrateur n'est requis pour cette copie. Il n'active ni RDP, ni règle de pare-feu, ni service système.

Si l'exécution du logiciel est bloquée par une politique Windows ou un antivirus, faire valider le binaire par l'administrateur du poste. Les EXE de cette livraison ne sont pas signés Authenticode.

## 3. Configurer le relais chez vous

1. Lancer **ConnexionPont.Relais.exe**.
2. Saisir votre **IP publique fixe** ou un nom DNS qui la désigne, sans `https://` et sans numéro de port.
3. Conserver **port Internet 443**, **port d'écoute du relais 8443**, **RDP local 13389**, **SSH local 10022**, sauf conflit de ports.
4. Renseigner l'IP interne ou le nom DNS du **serveur SSH**, tel qu'il est joignable depuis le PC client. Exemple : `172.20.58.14`. Le port cible est 22. Laisser vide désactive SSH.
5. L'utilisateur SSH est utilisé seulement pour le bouton et la commande à copier. Vous pouvez aussi le saisir directement dans votre terminal.
6. Cliquer **Enregistrer**. Les deux identités sont créées automatiquement ; aucune clé n'est préinstallée dans le ZIP.
7. Cliquer **Exporter l'agent**. Choisir un mot de passe d'au moins 12 caractères, puis enregistrer `client.rdptunnel`.

Transmettre au PC client `ConnexionPont.Agent.exe`, son `.config` et le fichier `.rdptunnel`. Communiquer le mot de passe du profil séparément. Ne pas transmettre la configuration locale du relais.

La configuration du relais est enregistrée dans `%LOCALAPPDATA%\ConnexionPont\Relais\settings.bin`, protégée pour votre compte Windows. Celle de l'agent est dans le dossier équivalent `Agent`.

## 4. Freebox et pare-feu de votre PC

1. Attribuer une IP LAN stable à votre PC relais, de préférence par un bail DHCP statique dans la Freebox. Exemple : `192.168.1.50`.
2. Dans Freebox OS, ouvrir les paramètres avancés et **Gestion des ports**. Créer la règle suivante :

| Champ | Valeur par défaut |
| --- | --- |
| IP de destination | IP LAN de votre PC relais, par exemple `192.168.1.50` |
| Protocole | TCP |
| Port de début externe | 443 |
| Port de fin externe | 443 |
| Port de destination interne | 8443 |

3. Vérifier que le port externe est disponible, notamment s'il sert déjà à l'administration distante de la Freebox ou à un autre équipement.
4. Vérifier que votre plage de ports IPv4 autorise le port 443. Chez Free, l'option **IPv4 fixe full-stack** donne accès à tous les ports. Sa demande peut changer votre IP : relever la nouvelle IP et la reporter dans le relais, puis réexporter le profil.
5. Ouvrir une fenêtre PowerShell **en administrateur** sur votre PC et autoriser uniquement le programme relais sur son port TLS :

```powershell
# Si installé par Installer.ps1 pour le même compte Windows
.\Configurer-PareFeu-Relais.ps1 -Port 8443

# Si utilisé en portable, donner le chemin absolu de l'EXE
.\Configurer-PareFeu-Relais.ps1 -Port 8443 -Programme 'C:\Outils\ConnexionPont\ConnexionPont.Relais.exe'
```

Ce script crée uniquement une règle entrante TCP pour le relais. Aucune modification automatique de votre Freebox n'est effectuée.

6. Lancer **Démarrer le relais**. Votre PC doit rester allumé, connecté à Internet et éveillé pendant l'utilisation.

## 5. Préparer le PC client

1. Si vous souhaitez le RDP : activer **Paramètres > Système > Bureau à distance** avec les droits administrateur nécessaires, conserver l'authentification NLA et autoriser votre compte Windows. ConnexionPont ne modifie pas cette configuration.
2. Vérifier que le PC peut joindre le serveur SSH sur le port 22. Depuis ce PC :

```powershell
Test-NetConnection -ComputerName IP_DU_SERVEUR_INTERNE -Port 22
```

3. Lancer **ConnexionPont.Agent.exe**, cliquer **Importer le profil**, sélectionner `.rdptunnel` et saisir son mot de passe.
4. Cliquer **Diagnostic** : vérifier l'accès RDP local et l'accès SSH. Un échec RDP n'empêche pas l'utilisation de SSH.
5. Cliquer **Connecter**. Les deux fenêtres doivent indiquer **CONNECTÉ**.

L'agent ouvre la connexion vers votre IP publique, sur le port externe 443. Aucun port entrant sur la box du client n'est nécessaire. Le réseau du client doit autoriser cette connexion TLS directe. Un proxy HTTP obligatoire ou une inspection TLS nécessite une adaptation ; cette V0.1 ne fournit pas de prise en charge HTTP CONNECT.

## 6. Utiliser RDP et SSH depuis chez vous

### RDP vers le PC client

Cliquer **Ouvrir RDP** dans le relais ou lancer :

```powershell
mstsc /v:127.0.0.1:13389
```

Utiliser le compte du **PC client**, par exemple `DOMAINE\utilisateur` ou `NOMPC\utilisateur`. Le tunnel ne remplace pas l'identification Windows. Une session RDP sur un poste Windows peut verrouiller/déconnecter l'affichage local ; ce n'est pas du partage d'écran simultané comme un outil d'assistance.

### SSH directement vers le serveur interne

Depuis votre PowerShell / Windows Terminal :

```powershell
ssh -p 10022 utilisateur@127.0.0.1

# Clé conservée sur votre ordinateur
ssh -i C:\Users\vous\.ssh\id_ed25519 -p 10022 utilisateur@127.0.0.1
```

Le bouton **Copier la commande SSH** fournit une variante avec un `HostKeyAlias` propre au profil. Cette variante évite de confondre les empreintes si plusieurs serveurs sont utilisés successivement sur le même port local. Vérifier l'empreinte SSH avec celle communiquée par l'administrateur du serveur lors de la première connexion. Le bouton **Ouvrir SSH** utilise `ssh.exe` du PC relais.

RDP et SSH peuvent être utilisés simultanément. Le serveur interne continue d'écouter sur son port **22**, pas sur 10022.

## 7. Reconnexion, démarrage et arrêt

- Si Internet ou le relais est interrompu, l'agent réessaie après 1, 2, 4 secondes, puis jusqu'à 30 secondes entre les tentatives.
- Une session RDP / SSH interrompue doit être rouverte après la reconnexion du tunnel.
- **Arrêter** ou fermer la fenêtre coupe les sessions. Fermer une session Windows arrête l'application ; verrouiller le poste conserve les processus.
- Pour démarrer la fenêtre à l'ouverture de votre session Windows, l'installation accepte `-DemarrerALOuvertureSession`. La connexion doit ensuite être lancée par le bouton. Cette version ne fonctionne pas avant l'ouverture de session Windows.

## 8. Diagnostic et recette réelle

| Symptôme | Vérification |
| --- | --- |
| Agent en attente / refus de connexion | IP publique actuelle, redirection Freebox, règle de pare-feu, relais démarré, PC éveillé |
| Connexion depuis le LAN échoue mais depuis l'extérieur fonctionne | Tester depuis un autre réseau : le comportement de rebouclage NAT dépend de la configuration Freebox |
| Port d'écoute déjà utilisé | Changer le port d'écoute du relais et le port interne de redirection ; le port externe peut rester 443 |
| Certificat refusé | Vérifier date/heure, profil correct, certificats non expirés. Ne pas supprimer leur validation |
| RDP échoue, tunnel connecté | Édition Windows du client, service RDP activé, accès `127.0.0.1:3389`, compte autorisé |
| SSH échoue, tunnel connecté | IP interne, résolution DNS côté client, `Test-NetConnection ... -Port 22`, service SSH et identifiants |
| Échec avec un proxy d'entreprise | Faire valider le transport TLS direct ; HTTP CONNECT non pris en charge dans la V0.1 |

Pour la recette, connecter le PC client depuis un réseau réellement externe à votre Freebox. Vérifier successivement l'agent connecté, une session RDP réelle, `hostname` dans une session SSH réelle, le transfert d'un fichier de test si nécessaire, puis l'arrêt/reprise du relais. Aucun de ces contrôles sur votre infrastructure n'est réalisé automatiquement par cette livraison.

## 9. Mise à jour, révocation et désinstallation

Pour mettre à jour : fermer les applications, conserver une copie de l'ancienne livraison, puis relancer `Installer.ps1`. Les configurations ne sont pas écrasées. Pour revenir en arrière, réinstaller l'ancienne livraison après fermeture des applications.

Pour changer le serveur SSH ou l'IP publique : arrêter les deux côtés, modifier/enregistrer le relais, réexporter le profil et le réimporter sur l'agent.

Pour révoquer un ancien agent ou renouveler les certificats : arrêter les deux côtés, supprimer explicitement la configuration du relais avec `Desinstaller.ps1 -Role Relais -EffacerConfiguration`, réinstaller/créer un nouvel appairage et importer le nouveau profil sur l'agent. L'ancien certificat n'est plus accepté par le nouveau relais.

```powershell
.\Desinstaller.ps1 -Role Agent
.\Desinstaller.ps1 -Role Relais

# Option explicite : supprimer aussi la configuration protégée
.\Desinstaller.ps1 -Role Agent -EffacerConfiguration
```

Retirer ensuite la redirection Freebox si elle n'est plus utilisée. Pour retirer la règle Windows créée par le script, dans PowerShell administrateur :

```powershell
Remove-NetFirewallRule -Name ConnexionPont-Relais-TLS
```

Les certificats d'appairage expirent après deux ans. Prévoir leur renouvellement par réappairage. Une sauvegarde de `settings.bin` reste liée au compte Windows / profil DPAPI ; elle n'est pas une sauvegarde portable vers un autre ordinateur. La perte du PC relais implique un nouvel appairage.

## Références

- Free : <https://assistance.free.fr/articles/1758> (IPv4 full-stack)
- Free : <https://assistance.free.fr/articles/1395> (redirection de ports)
- Microsoft : <https://learn.microsoft.com/windows-server/remote/remote-desktop-services/remotepc/remote-desktop-allow-access>
